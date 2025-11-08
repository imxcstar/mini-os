using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiniOS
{
    /// <summary>
    /// Full MiniC front-end: parses a practical C subset (ints, chars/strings, arrays, functions, control flow)
    /// and produces an executable program graph consumable by the MiniC runtime.
    /// </summary>
    public static class MiniCCompiler
    {
        public static MiniCProgram Compile(string source, MiniCCompilationOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new MiniCCompileException("C source is empty");
            var includeResolver = options?.IncludeResolver ?? MiniCIncludeResolver.Host;
            var sourcePath = options?.SourcePath;
            var preprocessor = new MiniCPreprocessor(includeResolver);
            var processed = preprocessor.Process(source, sourcePath);
            var parser = new MiniCParser(processed);
            return parser.ParseProgram();
        }
    }

    #region Parser infrastructure

    internal sealed class MiniCParser
    {
        private readonly Tokenizer _lexer;
        private Token _current;

        public MiniCParser(string text)
        {
            _lexer = new Tokenizer(text);
            _current = _lexer.NextToken();
        }

        public MiniCProgram ParseProgram()
        {
            var functions = new Dictionary<string, MiniCFunction>(StringComparer.Ordinal);
            var globals = new List<MiniCVarDecl>();
            while (!Check(TokenKind.Eof))
            {
                var baseType = ParseTypeSpecifier();
                var declarator = ParseDeclarator(baseType);
                if (Check(TokenKind.Symbol, "("))
                {
                    var func = ParseFunctionOrPrototype(baseType, declarator);
                    if (!func.HasBody)
                    {
                        if (!functions.ContainsKey(func.Name))
                            functions[func.Name] = func;
                        continue;
                    }
                    if (functions.TryGetValue(func.Name, out var existing) && existing.HasBody)
                        throw Error($"Function '{func.Name}' already defined");
                    functions[func.Name] = func;
                }
                else
                {
                    var decls = ParseGlobalDeclaration(baseType, declarator);
                    globals.AddRange(decls);
                }
            }
            if (!functions.ContainsKey("main"))
                throw Error("MiniC program must define int main()");
            return new MiniCProgram(functions, globals);
        }

        private MiniCFunction ParseFunctionOrPrototype(MiniCTypeSpecifier baseType, Declarator declarator)
        {
            Expect(TokenKind.Symbol, "(");
            var parameters = ParseParameterList();
            Expect(TokenKind.Symbol, ")");

            if (Match(TokenKind.Symbol, ";"))
            {
                return new MiniCFunction(declarator.Name, declarator.ResolveType(), parameters, new BlockStmt(new List<MiniCStatement>()), false);
            }

            var body = ParseBlock();
            return new MiniCFunction(declarator.Name, declarator.ResolveType(), parameters, body, true);
        }

        private List<MiniCVarDecl> ParseGlobalDeclaration(MiniCTypeSpecifier baseType, Declarator firstDecl)
        {
            var decls = new List<MiniCVarDecl>();
            decls.Add(BuildVarDecl(firstDecl, ParseOptionalInitializer()));
            while (Match(TokenKind.Symbol, ","))
            {
                var decl = ParseDeclarator(baseType);
                decls.Add(BuildVarDecl(decl, ParseOptionalInitializer()));
            }
            Expect(TokenKind.Symbol, ";");
            return decls;
        }

        private BlockStmt ParseBlock()
        {
            Expect(TokenKind.Symbol, "{");
            var list = new List<MiniCStatement>();
            while (!Match(TokenKind.Symbol, "}"))
            {
                if (IsTypeSpecifier(_current)) list.Add(ParseDeclaration());
                else list.Add(ParseStatement());
            }
            return new BlockStmt(list);
        }

        private MiniCStatement ParseDeclaration()
        {
            var baseType = ParseTypeSpecifier();
            var decls = new List<MiniCVarDecl>();
            do
            {
                var decl = ParseDeclarator(baseType);
                decls.Add(BuildVarDecl(decl, ParseOptionalInitializer()));
            }
            while (Match(TokenKind.Symbol, ","));
            Expect(TokenKind.Symbol, ";");
            return new VarDeclStmt(decls);
        }

        private Expr? ParseOptionalInitializer()
        {
            if (Match(TokenKind.Symbol, "=")) return ParseExpression();
            return null;
        }

        private MiniCVarDecl BuildVarDecl(Declarator decl, Expr? init)
        {
            var resolved = decl.ResolveType();
            if (resolved.Kind == MiniCTypeKind.Void)
                throw Error("Variables of type void are not supported");
            return new MiniCVarDecl(resolved, decl.Name, decl.IsArray, decl.ArrayLength, init);
        }

        private MiniCStatement ParseStatement()
        {
            if (MatchKeyword("if")) return ParseIf();
            if (MatchKeyword("while")) return ParseWhile();
            if (MatchKeyword("for")) return ParseFor();
            if (MatchKeyword("return")) return ParseReturn();
            if (MatchKeyword("break")) { Expect(TokenKind.Symbol, ";"); return new BreakStmt(); }
            if (MatchKeyword("continue")) { Expect(TokenKind.Symbol, ";"); return new ContinueStmt(); }
            if (Match(TokenKind.Symbol, "{"))
            {
                var inner = new List<MiniCStatement>();
                while (!Match(TokenKind.Symbol, "}"))
                {
                    if (IsTypeSpecifier(_current)) inner.Add(ParseDeclaration());
                    else inner.Add(ParseStatement());
                }
                return new BlockStmt(inner);
            }
            var expr = ParseExpression();
            Expect(TokenKind.Symbol, ";");
            return new ExprStmt(expr);
        }

        private MiniCStatement ParseIf()
        {
            Expect(TokenKind.Symbol, "(");
            var cond = ParseExpression();
            Expect(TokenKind.Symbol, ")");
            var thenStmt = ParseStatement();
            MiniCStatement? elseStmt = null;
            if (MatchKeyword("else")) elseStmt = ParseStatement();
            return new IfStmt(cond, thenStmt, elseStmt);
        }

        private MiniCStatement ParseWhile()
        {
            Expect(TokenKind.Symbol, "(");
            var cond = ParseExpression();
            Expect(TokenKind.Symbol, ")");
            var body = ParseStatement();
            return new WhileStmt(cond, body);
        }

        private MiniCStatement ParseFor()
        {
            Expect(TokenKind.Symbol, "(");
            MiniCStatement? init = null;
            if (!Check(TokenKind.Symbol, ";"))
            {
                if (IsTypeSpecifier(_current)) init = ParseDeclaration();
                else
                {
                    var expr = ParseExpression();
                    Expect(TokenKind.Symbol, ";");
                    init = new ExprStmt(expr);
                }
            }
            else Expect(TokenKind.Symbol, ";");

            Expr? cond = null;
            if (!Check(TokenKind.Symbol, ";")) cond = ParseExpression();
            Expect(TokenKind.Symbol, ";");

            Expr? post = null;
            if (!Check(TokenKind.Symbol, ")")) post = ParseExpression();
            Expect(TokenKind.Symbol, ")");

            var body = ParseStatement();
            return new ForStmt(init, cond, post, body);
        }

        private MiniCStatement ParseReturn()
        {
            if (Match(TokenKind.Symbol, ";")) return new ReturnStmt(null);
            var expr = ParseExpression();
            Expect(TokenKind.Symbol, ";");
            return new ReturnStmt(expr);
        }

        private List<MiniCParameter> ParseParameterList()
        {
            var list = new List<MiniCParameter>();
            if (Check(TokenKind.Symbol, ")")) return list;
            if (MatchKeyword("void") && Check(TokenKind.Symbol, ")")) return list;
            bool more;
            do
            {
                var type = ParseTypeSpecifier();
                var decl = ParseDeclarator(type);
                if (decl.IsArray) throw Error("Array parameters are not supported in MiniC");
                list.Add(new MiniCParameter(decl.Name, decl.ResolveType()));
                more = Match(TokenKind.Symbol, ",");
            }
            while (more);
            return list;
        }

        private Expr ParseExpression() => ParseAssignment();

        private Expr ParseAssignment()
        {
            var expr = ParseLogicalOr();
            if (Match(TokenKind.Symbol, "="))
                return new AssignmentExpr(expr, "=", ParseAssignment());
            if (Match(TokenKind.Symbol, "+="))
                return new AssignmentExpr(expr, "+=", ParseAssignment());
            if (Match(TokenKind.Symbol, "-="))
                return new AssignmentExpr(expr, "-=", ParseAssignment());
            if (Match(TokenKind.Symbol, "*="))
                return new AssignmentExpr(expr, "*=", ParseAssignment());
            if (Match(TokenKind.Symbol, "/="))
                return new AssignmentExpr(expr, "/=", ParseAssignment());
            if (Match(TokenKind.Symbol, "%="))
                return new AssignmentExpr(expr, "%=", ParseAssignment());
            return expr;
        }

        private Expr ParseLogicalOr()
        {
            var expr = ParseLogicalAnd();
            while (Match(TokenKind.Symbol, "||"))
                expr = new BinaryExpr("||", expr, ParseLogicalAnd());
            return expr;
        }

        private Expr ParseLogicalAnd()
        {
            var expr = ParseEquality();
            while (Match(TokenKind.Symbol, "&&"))
                expr = new BinaryExpr("&&", expr, ParseEquality());
            return expr;
        }

        private Expr ParseEquality()
        {
            var expr = ParseRelational();
            while (true)
            {
                if (Match(TokenKind.Symbol, "==")) expr = new BinaryExpr("==", expr, ParseRelational());
                else if (Match(TokenKind.Symbol, "!=")) expr = new BinaryExpr("!=", expr, ParseRelational());
                else break;
            }
            return expr;
        }

        private Expr ParseRelational()
        {
            var expr = ParseAdditive();
            while (true)
            {
                if (Match(TokenKind.Symbol, "<")) expr = new BinaryExpr("<", expr, ParseAdditive());
                else if (Match(TokenKind.Symbol, "<=")) expr = new BinaryExpr("<=", expr, ParseAdditive());
                else if (Match(TokenKind.Symbol, ">")) expr = new BinaryExpr(">", expr, ParseAdditive());
                else if (Match(TokenKind.Symbol, ">=")) expr = new BinaryExpr(">=", expr, ParseAdditive());
                else break;
            }
            return expr;
        }

        private Expr ParseAdditive()
        {
            var expr = ParseMultiplicative();
            while (true)
            {
                if (Match(TokenKind.Symbol, "+")) expr = new BinaryExpr("+", expr, ParseMultiplicative());
                else if (Match(TokenKind.Symbol, "-")) expr = new BinaryExpr("-", expr, ParseMultiplicative());
                else break;
            }
            return expr;
        }

        private Expr ParseMultiplicative()
        {
            var expr = ParseUnary();
            while (true)
            {
                if (Match(TokenKind.Symbol, "*")) expr = new BinaryExpr("*", expr, ParseUnary());
                else if (Match(TokenKind.Symbol, "/")) expr = new BinaryExpr("/", expr, ParseUnary());
                else if (Match(TokenKind.Symbol, "%")) expr = new BinaryExpr("%", expr, ParseUnary());
                else break;
            }
            return expr;
        }

        private Expr ParseUnary()
        {
            if (Match(TokenKind.Symbol, "!")) return new UnaryExpr("!", ParseUnary());
            if (Match(TokenKind.Symbol, "+")) return new UnaryExpr("+", ParseUnary());
            if (Match(TokenKind.Symbol, "-")) return new UnaryExpr("-", ParseUnary());
            if (Match(TokenKind.Symbol, "++")) return new PrefixUpdateExpr("++", ParseUnary());
            if (Match(TokenKind.Symbol, "--")) return new PrefixUpdateExpr("--", ParseUnary());
            if (Match(TokenKind.Symbol, "*")) return new DerefExpr(ParseUnary());
            return ParsePostfix();
        }

        private Expr ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                if (Match(TokenKind.Symbol, "++")) expr = new PostfixUpdateExpr("++", expr);
                else if (Match(TokenKind.Symbol, "--")) expr = new PostfixUpdateExpr("--", expr);
                else if (Match(TokenKind.Symbol, "("))
                {
                    var args = new List<Expr>();
                    if (!Check(TokenKind.Symbol, ")"))
                    {
                        do { args.Add(ParseExpression()); }
                        while (Match(TokenKind.Symbol, ","));
                    }
                    Expect(TokenKind.Symbol, ")");
                    if (expr is not VarExpr ve)
                        throw Error("Only simple function names supported in calls");
                    expr = new CallExpr(ve.Name, args);
                }
                else if (Match(TokenKind.Symbol, "["))
                {
                    var index = ParseExpression();
                    Expect(TokenKind.Symbol, "]");
                    if (expr is VarExpr ve)
                        expr = new ArrayAccessExpr(ve.Name, index);
                    else
                        expr = new PointerIndexExpr(expr, index);
                }
                else break;
            }
            return expr;
        }

        private Expr ParsePrimary()
        {
            if (Match(TokenKind.Symbol, "("))
            {
                var inner = ParseExpression();
                Expect(TokenKind.Symbol, ")");
                return inner;
            }
            if (Match(TokenKind.Number, out var numberTok))
            {
                int value = ParseNumber(numberTok.Text);
                return new IntLiteralExpr(value);
            }
            if (Match(TokenKind.String, out var strTok))
                return new StringLiteralExpr(UnescapeString(strTok.Text));
            if (Match(TokenKind.Char, out var charTok))
                return new IntLiteralExpr(UnescapeChar(charTok.Text));
            if (Match(TokenKind.Identifier, out var idTok))
            {
                return new VarExpr(idTok.Text);
            }
            throw Error($"Unexpected token '{_current.Text}'");
        }

        private MiniCTypeSpecifier ParseTypeSpecifier()
        {
            while (IsQualifier(_current)) _current = _lexer.NextToken();
            if (MatchKeyword("int")) return new MiniCTypeSpecifier(MiniCTypeKind.Int);
            if (MatchKeyword("char")) return new MiniCTypeSpecifier(MiniCTypeKind.Char);
            if (MatchKeyword("void")) return new MiniCTypeSpecifier(MiniCTypeKind.Void);
            throw Error("Type specifier expected");
        }

        private Declarator ParseDeclarator(MiniCTypeSpecifier baseType)
        {
            int pointerDepth = 0;
            while (Match(TokenKind.Symbol, "*")) pointerDepth++;
            var nameTok = Expect(TokenKind.Identifier);
            bool isArray = false;
            int arrayLen = 0;
            if (Match(TokenKind.Symbol, "["))
            {
                var lenTok = Expect(TokenKind.Number);
                arrayLen = ParseNumber(lenTok.Text);
                if (arrayLen <= 0) throw Error("Array length must be positive");
                Expect(TokenKind.Symbol, "]");
                isArray = true;
            }
            return new Declarator(nameTok.Text, baseType, pointerDepth, isArray, arrayLen);
        }

        private static int ParseNumber(string text)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        private static string UnescapeString(string literal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < literal.Length; i++)
            {
                var ch = literal[i];
                if (ch == '\\' && i + 1 < literal.Length)
                {
                    var next = literal[++i];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        '\'' => '\'',
                        _ => next
                    });
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static int UnescapeChar(string literal)
        {
            if (literal.Length == 0) return 0;
            if (literal[0] == '\\' && literal.Length > 1)
            {
                return literal[1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"' => '"',
                    _ => literal[1]
                };
            }
            return literal[0];
        }

        private bool IsTypeSpecifier(Token token)
        {
            if (token.Kind == TokenKind.Identifier)
            {
                var text = token.Text;
                return text is "int" or "char" or "void" or "const" or "unsigned" or "signed" or "static" or "extern" or "short" or "long";
            }
            return false;
        }

        private static bool IsQualifier(Token token)
        {
            if (token.Kind != TokenKind.Identifier) return false;
            return token.Text is "const" or "unsigned" or "signed" or "static" or "extern" or "short" or "long";
        }

        private bool MatchKeyword(string text)
        {
            if (_current.Kind == TokenKind.Identifier && _current.Text == text)
            {
                _current = _lexer.NextToken();
                return true;
            }
            return false;
        }

        private bool Match(TokenKind kind, string symbol)
        {
            if (_current.Kind == kind && _current.Text == symbol)
            {
                _current = _lexer.NextToken();
                return true;
            }
            return false;
        }

        private bool Match(TokenKind kind, out Token token)
        {
            if (_current.Kind == kind)
            {
                token = _current;
                _current = _lexer.NextToken();
                return true;
            }
            token = default;
            return false;
        }

        private bool Check(TokenKind kind, string? symbol = null) =>
            _current.Kind == kind && (symbol == null || _current.Text == symbol);

        private Token Expect(TokenKind kind, string? symbol = null)
        {
            if (!Check(kind, symbol))
                throw Error(symbol is null ? $"Expected {kind}" : $"Expected '{symbol}'");
            var tok = _current;
            _current = _lexer.NextToken();
            return tok;
        }

        private MiniCCompileException Error(string message)
        {
            var token = _current;
            var context = string.IsNullOrEmpty(token.Text) ? token.Kind.ToString() : token.Text;
            return new MiniCCompileException($"{message} (at '{context}' pos {token.Position})");
        }
    }

    internal sealed class Tokenizer
    {
        private readonly string _text;
        private int _index;
        public Token Previous { get; private set; }

        private static readonly string[] Symbols = new[]
        {
            "++","--","<=",">=","==","!=","&&","||","+=","-=","*=","/=","%=",
            "{","}","(",")","[","]",";",",","+","-","*","/","%","<",">","=","!","&","|"
        };

        public Tokenizer(string text) => _text = text;

        public Token NextToken()
        {
            SkipWhitespace();
            if (_index >= _text.Length)
                return Previous = new Token(TokenKind.Eof, string.Empty, _index);

            char ch = _text[_index];
            if (char.IsLetter(ch) || ch == '_')
            {
                int start = _index++;
                while (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_')) _index++;
                return Previous = new Token(TokenKind.Identifier, _text[start.._index], start);
            }
            if (char.IsDigit(ch))
            {
                int start = _index++;
                if (ch == '0' && _index < _text.Length && (_text[_index] == 'x' || _text[_index] == 'X'))
                {
                    _index++;
                    while (_index < _text.Length && IsHex(_text[_index])) _index++;
                }
                else
                {
                    while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
                }
                return Previous = new Token(TokenKind.Number, _text[start.._index], start);
            }
            if (ch == '\'')
            {
                int start = ++_index;
                if (_index >= _text.Length) throw new MiniCCompileException("Unterminated char literal");
                if (_text[_index] == '\\') _index += 2;
                else _index++;
                if (_index >= _text.Length || _text[_index] != '\'') throw new MiniCCompileException("Unterminated char literal");
                var literal = _text[start.._index];
                _index++;
                return Previous = new Token(TokenKind.Char, literal, start - 1);
            }
            if (ch == '"')
            {
                int start = ++_index;
                while (_index < _text.Length && _text[_index] != '"')
                {
                    if (_text[_index] == '\\' && _index + 1 < _text.Length)
                        _index += 2;
                    else
                        _index++;
                }
                if (_index >= _text.Length) throw new MiniCCompileException("Unterminated string literal");
                var literal = _text[start.._index];
                _index++;
                return Previous = new Token(TokenKind.String, literal, start - 1);
            }
            foreach (var sym in Symbols)
            {
                if (_text.AsSpan(_index).StartsWith(sym, StringComparison.Ordinal))
                {
                    _index += sym.Length;
                    return Previous = new Token(TokenKind.Symbol, sym, _index - sym.Length);
                }
            }
            throw new MiniCCompileException($"Unexpected character '{ch}'");
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                var ch = _text[_index];
                if (char.IsWhiteSpace(ch)) { _index++; continue; }
                if (ch == '/' && _index + 1 < _text.Length)
                {
                    if (_text[_index + 1] == '/')
                    {
                        _index += 2;
                        while (_index < _text.Length && _text[_index] != '\n') _index++;
                        continue;
                    }
                    if (_text[_index + 1] == '*')
                    {
                        _index += 2;
                        while (_index + 1 < _text.Length && !(_text[_index] == '*' && _text[_index + 1] == '/')) _index++;
                        _index += 2;
                        continue;
                    }
                }
                if (ch == '#')
                {
                    while (_index < _text.Length && _text[_index] != '\n') _index++;
                    continue;
                }
                break;
            }
        }

        private static bool IsHex(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    internal enum TokenKind { Identifier, Number, String, Char, Symbol, Eof }

    internal readonly record struct Token(TokenKind Kind, string Text, int Position);

    internal sealed record MiniCTypeSpecifier(MiniCTypeKind Kind);

    internal sealed record Declarator(string Name, MiniCTypeSpecifier BaseType, int PointerDepth, bool IsArray, int ArrayLength)
    {
        public MiniCVarType ResolveType()
        {
            MiniCVarType resolved = BaseType.Kind switch
            {
                MiniCTypeKind.Int => MiniCVarType.Int,
                MiniCTypeKind.Char => MiniCVarType.Char,
                MiniCTypeKind.Void => MiniCVarType.Void,
                _ => throw new MiniCCompileException("unsupported base type")
            };
            for (int i = 0; i < PointerDepth; i++)
            {
                resolved = MiniCVarType.PointerTo(resolved);
            }
            if (IsArray && resolved.Kind == MiniCTypeKind.Void && !resolved.IsPointer)
                throw new MiniCCompileException("void arrays are not allowed");
            return resolved;
        }
    }

    #endregion

    #region AST

    public sealed class MiniCProgram
    {
        public IReadOnlyDictionary<string, MiniCFunction> Functions { get; }
        public IReadOnlyList<MiniCVarDecl> Globals { get; }
        public MiniCFunction EntryPoint { get; }

        public MiniCProgram(Dictionary<string, MiniCFunction> functions, IReadOnlyList<MiniCVarDecl> globals)
        {
            Functions = functions;
            Globals = globals;
            if (!functions.TryGetValue("main", out var main))
                throw new MiniCCompileException("main function missing");
            if (!main.HasBody)
                throw new MiniCCompileException("main must have a body");
            EntryPoint = main;
        }
    }

    public sealed record MiniCFunction(string Name, MiniCVarType ReturnType, IReadOnlyList<MiniCParameter> Parameters, BlockStmt Body, bool HasBody)
    {
        public bool IsVoid => ReturnType.Kind == MiniCTypeKind.Void && !ReturnType.IsPointer;
    }

    public sealed record MiniCParameter(string Name, MiniCVarType Type);

    public enum MiniCTypeKind { Void, Int, Char, Pointer }

    public sealed record MiniCVarType(MiniCTypeKind Kind, MiniCVarType? ElementType = null)
    {
        public static MiniCVarType Void { get; } = new(MiniCTypeKind.Void);
        public static MiniCVarType Int { get; } = new(MiniCTypeKind.Int);
        public static MiniCVarType Char { get; } = new(MiniCTypeKind.Char);
        public static MiniCVarType PointerTo(MiniCVarType element) => new(MiniCTypeKind.Pointer, element);
        public bool IsNumeric => Kind is MiniCTypeKind.Int or MiniCTypeKind.Char;
        public bool IsPointer => Kind == MiniCTypeKind.Pointer;
        public int SizeInBytes => Kind switch
        {
            MiniCTypeKind.Char => 1,
            MiniCTypeKind.Int => 4,
            MiniCTypeKind.Pointer => 4,
            _ => 0
        };
    }

    public sealed record MiniCVarDecl(MiniCVarType Type, string Name, bool IsArray, int ArrayLength, Expr? Initializer);

    public abstract record MiniCStatement;
    public sealed record BlockStmt(List<MiniCStatement> Statements) : MiniCStatement;
    public sealed record VarDeclStmt(List<MiniCVarDecl> Decls) : MiniCStatement;
    public sealed record ExprStmt(Expr Expression) : MiniCStatement;
    public sealed record IfStmt(Expr Condition, MiniCStatement Then, MiniCStatement? Else) : MiniCStatement;
    public sealed record WhileStmt(Expr Condition, MiniCStatement Body) : MiniCStatement;
    public sealed record ForStmt(MiniCStatement? Init, Expr? Condition, Expr? Post, MiniCStatement Body) : MiniCStatement;
    public sealed record ReturnStmt(Expr? Expression) : MiniCStatement;
    public sealed record BreakStmt() : MiniCStatement;
    public sealed record ContinueStmt() : MiniCStatement;

    public abstract record Expr
    {
        public virtual bool CanAssign => false;
        public virtual MiniCLValue GetReference(MiniCEvalContext ctx) => throw new MiniCRuntimeException("Expression is not assignable");
        public abstract MiniCValue Evaluate(MiniCEvalContext ctx);
    }

    public sealed record IntLiteralExpr(int Value) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx) => MiniCValue.FromInt(Value);
    }

    public sealed record StringLiteralExpr(string Value) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx) => MiniCValue.FromString(Value);
    }

    public sealed record VarExpr(string Name) : Expr
    {
        public override bool CanAssign => true;
        public override MiniCLValue GetReference(MiniCEvalContext ctx)
        {
            var variable = ctx.ResolveVariable(Name);
            if (variable.IsArray)
                throw new MiniCRuntimeException("Cannot assign entire array");
            return new MiniCLValue(ctx, variable, null);
        }
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            var variable = ctx.ResolveVariable(Name);
            if (variable.IsArray)
            {
                if (variable.Type.Kind != MiniCTypeKind.Char)
                    throw new MiniCRuntimeException("Array value cannot be used as r-value");
                return MiniCValue.FromString(variable.ReadArrayAsString());
            }
            return variable.GetValue();
        }
    }

    public sealed record ArrayAccessExpr(string Name, Expr Index) : Expr
    {
        public override bool CanAssign => true;
        public override MiniCLValue GetReference(MiniCEvalContext ctx)
        {
            var variable = ctx.ResolveVariable(Name);
            var idx = Index.Evaluate(ctx).AsInt();
            if (variable.IsArray)
                return new MiniCLValue(ctx, variable, idx);
            if (variable.Type.IsPointer)
            {
                var pointer = variable.GetValue().AsPointer();
                return new MiniCLValue(ctx, pointer.Offset(idx));
            }
            throw new MiniCRuntimeException($"Variable '{Name}' is not an array");
        }
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
            => GetReference(ctx).Get();
    }

    public sealed record PointerIndexExpr(Expr Pointer, Expr Index) : Expr
    {
        public override bool CanAssign => true;
        public override MiniCLValue GetReference(MiniCEvalContext ctx)
        {
            var basePtr = Pointer.Evaluate(ctx).AsPointer();
            var offset = Index.Evaluate(ctx).AsInt();
            return new MiniCLValue(ctx, basePtr.Offset(offset));
        }
        public override MiniCValue Evaluate(MiniCEvalContext ctx) => GetReference(ctx).Get();
    }

    public sealed record DerefExpr(Expr Operand) : Expr
    {
        public override bool CanAssign => true;
        public override MiniCLValue GetReference(MiniCEvalContext ctx)
        {
            var pointer = Operand.Evaluate(ctx).AsPointer();
            return new MiniCLValue(ctx, pointer);
        }
        public override MiniCValue Evaluate(MiniCEvalContext ctx) => GetReference(ctx).Get();
    }

    public sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            if (Op == "&&")
            {
                var left = Left.Evaluate(ctx).AsInt();
                if (left == 0) return MiniCValue.FromInt(0);
                return MiniCValue.FromInt(Right.Evaluate(ctx).AsInt() != 0 ? 1 : 0);
            }
            if (Op == "||")
            {
                var left = Left.Evaluate(ctx).AsInt();
                if (left != 0) return MiniCValue.FromInt(1);
                return MiniCValue.FromInt(Right.Evaluate(ctx).AsInt() != 0 ? 1 : 0);
            }
            var l = Left.Evaluate(ctx);
            var r = Right.Evaluate(ctx);
            if (Op == "+" && l.Kind == MiniCValueKind.Pointer && r.Kind == MiniCValueKind.Int)
                return MiniCValue.FromPointer(l.AsPointer().Offset(r.AsInt()));
            if (Op == "+" && l.Kind == MiniCValueKind.Int && r.Kind == MiniCValueKind.Pointer)
                return MiniCValue.FromPointer(r.AsPointer().Offset(l.AsInt()));
            if (Op == "-" && l.Kind == MiniCValueKind.Pointer && r.Kind == MiniCValueKind.Int)
                return MiniCValue.FromPointer(l.AsPointer().Offset(-r.AsInt()));
            if (Op == "-" && l.Kind == MiniCValueKind.Pointer && r.Kind == MiniCValueKind.Pointer)
                return MiniCValue.FromInt(l.AsInt() - r.AsInt());
            return Op switch
            {
                "+" => MiniCValue.FromInt(l.AsInt() + r.AsInt()),
                "-" => MiniCValue.FromInt(l.AsInt() - r.AsInt()),
                "*" => MiniCValue.FromInt(l.AsInt() * r.AsInt()),
                "/" => MiniCValue.FromInt(r.AsInt() == 0 ? 0 : l.AsInt() / r.AsInt()),
                "%" => MiniCValue.FromInt(r.AsInt() == 0 ? 0 : l.AsInt() % r.AsInt()),
                "<" => MiniCValue.FromInt(l.AsInt() < r.AsInt() ? 1 : 0),
                "<=" => MiniCValue.FromInt(l.AsInt() <= r.AsInt() ? 1 : 0),
                ">" => MiniCValue.FromInt(l.AsInt() > r.AsInt() ? 1 : 0),
                ">=" => MiniCValue.FromInt(l.AsInt() >= r.AsInt() ? 1 : 0),
                "==" => CompareEq(l, r),
                "!=" => MiniCValue.FromInt(CompareEq(l, r).AsInt() == 1 ? 0 : 1),
                _ => throw new MiniCRuntimeException($"Unsupported operator '{Op}'")
            };
        }

        private static MiniCValue CompareEq(MiniCValue left, MiniCValue right)
        {
            if (left.Kind == MiniCValueKind.Pointer || right.Kind == MiniCValueKind.Pointer)
            {
                if (left.Kind == MiniCValueKind.Pointer && right.Kind == MiniCValueKind.Pointer)
                {
                    var equals = PointersEqual(left.AsPointer(), right.AsPointer());
                    return MiniCValue.FromInt(equals ? 1 : 0);
                }
                if ((left.Kind == MiniCValueKind.Pointer && right.Kind == MiniCValueKind.String) ||
                    (left.Kind == MiniCValueKind.String && right.Kind == MiniCValueKind.Pointer))
                {
                    return MiniCValue.FromInt(string.Equals(left.AsString(), right.AsString(), StringComparison.Ordinal) ? 1 : 0);
                }
                return MiniCValue.FromInt(left.AsInt() == right.AsInt() ? 1 : 0);
            }
            if (left.Kind == MiniCValueKind.String || right.Kind == MiniCValueKind.String)
            {
                var l = left.Kind == MiniCValueKind.String ? left.AsString() : left.AsInt().ToString(CultureInfo.InvariantCulture);
                var r = right.Kind == MiniCValueKind.String ? right.AsString() : right.AsInt().ToString(CultureInfo.InvariantCulture);
                return MiniCValue.FromInt(string.Equals(l, r, StringComparison.Ordinal) ? 1 : 0);
            }
            return MiniCValue.FromInt(left.AsInt() == right.AsInt() ? 1 : 0);
        }

        private static bool PointersEqual(MiniCPointer a, MiniCPointer b)
        {
            if (a.IsNull && b.IsNull) return true;
            if (a.IsNull || b.IsNull) return false;
            return ReferenceEquals(a.Memory, b.Memory) && a.Address == b.Address;
        }
    }

    public sealed record UnaryExpr(string Op, Expr Operand) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            var v = Operand.Evaluate(ctx);
            return Op switch
            {
                "!" => MiniCValue.FromInt(v.AsInt() == 0 ? 1 : 0),
                "+" => v,
                "-" => MiniCValue.FromInt(-v.AsInt()),
                _ => throw new MiniCRuntimeException($"Unsupported unary operator {Op}")
            };
        }
    }

    public sealed record PrefixUpdateExpr(string Op, Expr Operand) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            if (!Operand.CanAssign) throw new MiniCRuntimeException("Operand is not assignable");
            var target = Operand.GetReference(ctx);
            var value = target.Get();
            var newValue = Op switch
            {
                "++" => MiniCValue.FromInt(value.AsInt() + 1),
                "--" => MiniCValue.FromInt(value.AsInt() - 1),
                _ => throw new MiniCRuntimeException($"Unsupported operator {Op}")
            };
            target.Set(newValue);
            return newValue;
        }
    }

    public sealed record PostfixUpdateExpr(string Op, Expr Operand) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            if (!Operand.CanAssign) throw new MiniCRuntimeException("Operand is not assignable");
            var target = Operand.GetReference(ctx);
            var value = target.Get();
            var newValue = Op switch
            {
                "++" => MiniCValue.FromInt(value.AsInt() + 1),
                "--" => MiniCValue.FromInt(value.AsInt() - 1),
                _ => throw new MiniCRuntimeException($"Unsupported operator {Op}")
            };
            target.Set(newValue);
            return value;
        }
    }

    public sealed record AssignmentExpr(Expr Target, string Op, Expr Value) : Expr
    {
        public override bool CanAssign => true;
        public override MiniCLValue GetReference(MiniCEvalContext ctx) => Target.GetReference(ctx);
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            if (!Target.CanAssign) throw new MiniCRuntimeException("Invalid assignment target");
            var reference = Target.GetReference(ctx);
            var rhs = Value.Evaluate(ctx);
            if (Op == "=")
            {
                reference.Set(rhs);
                return rhs;
            }
            var current = reference.Get().AsInt();
            var amount = rhs.AsInt();
            var result = Op switch
            {
                "+=" => MiniCValue.FromInt(current + amount),
                "-=" => MiniCValue.FromInt(current - amount),
                "*=" => MiniCValue.FromInt(current * amount),
                "/=" => MiniCValue.FromInt(amount == 0 ? 0 : current / amount),
                "%=" => MiniCValue.FromInt(amount == 0 ? 0 : current % amount),
                _ => throw new MiniCRuntimeException($"Unsupported assignment operator {Op}")
            };
            reference.Set(result);
            return result;
        }
    }

    public sealed record CallExpr(string Name, IReadOnlyList<Expr> Arguments) : Expr
    {
        public override MiniCValue Evaluate(MiniCEvalContext ctx)
        {
            var args = new List<MiniCValue>(Arguments.Count);
            foreach (var arg in Arguments)
                args.Add(arg.Evaluate(ctx));
            return ctx.CallFunction(Name, args);
        }
    }

    #endregion

    public sealed class MiniCCompileException : Exception
    {
        public MiniCCompileException(string message) : base(message) { }
    }
}
