using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public sealed class MiniCRuntime
    {
        private readonly MiniCProgram _program;
        private readonly SysApi _sys;

        public MiniCRuntime(MiniCProgram program, SysApi sys)
        {
            _program = program;
            _sys = sys;
        }

        public Task<int> RunAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

        public int Run(CancellationToken ct)
        {
            var ctx = new MiniCEvalContext(_program, _sys, ct);
            var result = ctx.CallFunction("main", Array.Empty<MiniCValue>());
            return result.Kind == MiniCValueKind.Int ? result.AsInt() : 0;
        }
    }

    public sealed class MiniCEvalContext
    {
        private readonly MiniCProgram _program;
        private readonly SysApi _sys;
        private readonly CancellationToken _ct;
        private readonly Stack<MiniCStackFrame> _frames = new();

        public MiniCEvalContext(MiniCProgram program, SysApi sys, CancellationToken ct)
        {
            _program = program;
            _sys = sys;
            _ct = ct;
        }

        public SysApi Sys => _sys;
        public CancellationToken CancellationToken => _ct;

        public MiniCValue CallFunction(string name, IReadOnlyList<MiniCValue> args)
        {
            if (_program.Functions.TryGetValue(name, out var fn) && fn.HasBody)
            {
                return InvokeUserFunction(fn, args);
            }
            if (MiniCBuiltins.TryInvoke(name, this, args, out var builtinValue))
                return builtinValue;
            if (_program.Functions.TryGetValue(name, out var proto) && !proto.HasBody)
                throw new MiniCRuntimeException($"Function '{name}' declared but not defined");
            throw new MiniCRuntimeException($"Unknown function '{name}'");
        }

        private MiniCValue InvokeUserFunction(MiniCFunction fn, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != fn.Parameters.Count)
                throw new MiniCRuntimeException($"Function '{fn.Name}' expects {fn.Parameters.Count} args but got {args.Count}");
            var frame = new MiniCStackFrame(fn);
            _frames.Push(frame);
            try
            {
                for (int i = 0; i < fn.Parameters.Count; i++)
                {
                    var param = fn.Parameters[i];
                    var variable = frame.Declare(param.Name, param.Type, false, 0);
                    AssignValue(variable, args[i]);
                }
                ExecuteBlock(fn.Body);
                return fn.ReturnType.Kind == MiniCTypeKind.Void ? MiniCValue.Void : MiniCValue.FromInt(0);
            }
            catch (MiniCReturnSignal ret)
            {
                return ValidateReturn(fn, ret.Value);
            }
            finally
            {
                _frames.Pop();
            }
        }

        private MiniCValue ValidateReturn(MiniCFunction fn, MiniCValue value)
        {
            if (fn.ReturnType.Kind == MiniCTypeKind.Void)
            {
                if (value.Kind != MiniCValueKind.Void)
                    throw new MiniCRuntimeException($"Function '{fn.Name}' returns void but value provided");
                return MiniCValue.Void;
            }
            if (fn.ReturnType.Kind == MiniCTypeKind.String)
            {
                if (value.Kind != MiniCValueKind.String)
                    throw new MiniCRuntimeException($"Function '{fn.Name}' must return string");
                return value;
            }
            if (value.Kind == MiniCValueKind.Void)
                return MiniCValue.FromInt(0);
            return MiniCValue.FromInt(value.AsInt());
        }

        public MiniCVariable ResolveVariable(string name)
        {
            if (_frames.TryPeek(out var frame))
                return frame.Resolve(name);
            throw new MiniCRuntimeException($"Variable '{name}' not found");
        }

        public void ExecuteBlock(BlockStmt block)
        {
            if (!_frames.TryPeek(out var frame))
                throw new MiniCRuntimeException("No active frame");
            frame.PushScope();
            try
            {
                foreach (var stmt in block.Statements)
                    ExecuteStatement(stmt);
            }
            finally
            {
                frame.PopScope();
            }
        }

        private void ExecuteStatement(MiniCStatement stmt)
        {
            _ct.ThrowIfCancellationRequested();
            switch (stmt)
            {
                case BlockStmt block:
                    ExecuteBlock(block);
                    break;
                case VarDeclStmt decl:
                    foreach (var d in decl.Decls)
                        DeclareVariable(d);
                    break;
                case ExprStmt est:
                    est.Expression.Evaluate(this);
                    break;
                case IfStmt ifs:
                {
                    var cond = ifs.Condition.Evaluate(this).AsInt();
                    if (cond != 0) ExecuteStatement(ifs.Then);
                    else if (ifs.Else != null) ExecuteStatement(ifs.Else);
                    break;
                }
                case WhileStmt wh:
                    ExecuteWhile(wh);
                    break;
                case ForStmt fs:
                    ExecuteFor(fs);
                    break;
                case ReturnStmt ret:
                {
                    if (ret.Expression is null) throw new MiniCReturnSignal(MiniCValue.Void);
                    var val = ret.Expression.Evaluate(this);
                    throw new MiniCReturnSignal(val);
                }
                case BreakStmt:
                    throw MiniCBreakSignal.Instance;
                case ContinueStmt:
                    throw MiniCContinueSignal.Instance;
                default:
                    throw new MiniCRuntimeException($"Unsupported statement type {stmt.GetType().Name}");
            }
        }

        private void ExecuteWhile(WhileStmt stmt)
        {
            while (true)
            {
                if (stmt.Condition.Evaluate(this).AsInt() == 0) break;
                try
                {
                    ExecuteStatement(stmt.Body);
                }
                catch (MiniCContinueSignal)
                {
                    continue;
                }
                catch (MiniCBreakSignal)
                {
                    break;
                }
            }
        }

        private void ExecuteFor(ForStmt stmt)
        {
            if (!_frames.TryPeek(out var frame))
                throw new MiniCRuntimeException("No active frame");
            frame.PushScope();
            try
            {
                if (stmt.Init != null) ExecuteStatement(stmt.Init);
                while (true)
                {
                    if (stmt.Condition != null && stmt.Condition.Evaluate(this).AsInt() == 0)
                        break;
                    try
                    {
                        ExecuteStatement(stmt.Body);
                    }
                    catch (MiniCContinueSignal)
                    {
                        stmt.Post?.Evaluate(this);
                        continue;
                    }
                    catch (MiniCBreakSignal)
                    {
                        break;
                    }
                    stmt.Post?.Evaluate(this);
                }
            }
            finally
            {
                frame.PopScope();
            }
        }

        private void DeclareVariable(MiniCVarDecl decl)
        {
            if (!_frames.TryPeek(out var frame))
                throw new MiniCRuntimeException("No active frame");
            var variable = frame.Declare(decl.Name, decl.Type, decl.IsArray, decl.ArrayLength);
            if (decl.IsArray)
            {
                if (decl.Initializer != null)
                {
                    if (decl.Type.Kind == MiniCTypeKind.Char && decl.Initializer is StringLiteralExpr literal)
                    {
                        variable.WriteStringToArray(literal.Value);
                    }
                    else
                    {
                        var init = decl.Initializer.Evaluate(this);
                        if (decl.Type.Kind == MiniCTypeKind.Char && init.Kind == MiniCValueKind.String)
                            variable.WriteStringToArray(init.AsString());
                        else
                            throw new MiniCRuntimeException("Array initialiser must be a string literal");
                    }
                }
                return;
            }
            if (decl.Initializer != null)
                AssignValue(variable, decl.Initializer.Evaluate(this));
            else if (decl.Type.IsString)
                variable.SetValue(MiniCValue.FromString(string.Empty));
            else
                variable.SetValue(MiniCValue.FromInt(0));
        }

        private static void AssignValue(MiniCVariable variable, MiniCValue value)
        {
            if (variable.IsArray)
                throw new MiniCRuntimeException("Cannot assign entire array");
            if (variable.Type.Kind == MiniCTypeKind.String)
            {
                if (value.Kind != MiniCValueKind.String)
                    throw new MiniCRuntimeException("Expected string value");
                variable.SetValue(value);
                return;
            }
            variable.SetValue(value.Kind == MiniCValueKind.Void ? MiniCValue.FromInt(0) : MiniCValue.FromInt(value.AsInt()));
        }
    }

    internal sealed class MiniCStackFrame
    {
        private readonly List<Dictionary<string, MiniCVariable>> _scopes = new();
        public MiniCFunction Function { get; }

        public MiniCStackFrame(MiniCFunction fn)
        {
            Function = fn;
            _scopes.Add(new Dictionary<string, MiniCVariable>(StringComparer.Ordinal));
        }

        public void PushScope() => _scopes.Add(new Dictionary<string, MiniCVariable>(StringComparer.Ordinal));

        public void PopScope()
        {
            if (_scopes.Count == 1)
                throw new MiniCRuntimeException("Cannot pop function root scope");
            _scopes.RemoveAt(_scopes.Count - 1);
        }

        public MiniCVariable Declare(string name, MiniCVarType type, bool isArray, int arrayLength)
        {
            var scope = _scopes[^1];
            if (scope.ContainsKey(name))
                throw new MiniCRuntimeException($"Identifier '{name}' already declared in this scope");
            var variable = new MiniCVariable(name, type, isArray, arrayLength);
            scope[name] = variable;
            return variable;
        }

        public MiniCVariable Resolve(string name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out var variable))
                    return variable;
            }
            throw new MiniCRuntimeException($"Unknown identifier '{name}'");
        }
    }

    public sealed class MiniCVariable
    {
        private readonly int[]? _array;
        private MiniCValue _value;

        public string Name { get; }
        public MiniCVarType Type { get; }
        public bool IsArray { get; }
        public int ArrayLength { get; }

        public MiniCVariable(string name, MiniCVarType type, bool isArray, int arrayLength)
        {
            Name = name;
            Type = type;
            IsArray = isArray;
            if (isArray)
            {
                if (arrayLength <= 0) throw new MiniCRuntimeException($"Array '{name}' must have positive length");
                if (!type.IsNumeric && type.Kind != MiniCTypeKind.Char)
                    throw new MiniCRuntimeException("Only numeric/char arrays supported");
                ArrayLength = arrayLength;
                _array = new int[arrayLength];
            }
            else
            {
                ArrayLength = 0;
                _value = type.Kind == MiniCTypeKind.String ? MiniCValue.FromString(string.Empty) : MiniCValue.FromInt(0);
            }
        }

        public MiniCValue GetValue()
        {
            if (IsArray) throw new MiniCRuntimeException("Array cannot be used as scalar value");
            return _value;
        }

        public void SetValue(MiniCValue value)
        {
            if (IsArray) throw new MiniCRuntimeException("Array cannot be assigned directly");
            if (Type.Kind == MiniCTypeKind.String)
            {
                if (value.Kind != MiniCValueKind.String)
                    throw new MiniCRuntimeException("Expected string assignment");
                _value = value;
            }
            else
            {
                var intValue = value.Kind == MiniCValueKind.String ? throw new MiniCRuntimeException("Cannot assign string to numeric variable") : value.AsInt();
                _value = MiniCValue.FromInt(intValue);
            }
        }

        public int GetArrayElement(int index)
        {
            if (_array is null) throw new MiniCRuntimeException("Variable is not an array");
            if ((uint)index >= _array.Length) throw new MiniCRuntimeException($"Array index {index} out of range for '{Name}'");
            return _array[index];
        }

        public void SetArrayElement(int index, int value)
        {
            if (_array is null) throw new MiniCRuntimeException("Variable is not an array");
            if ((uint)index >= _array.Length) throw new MiniCRuntimeException($"Array index {index} out of range for '{Name}'");
            _array[index] = Type.Kind == MiniCTypeKind.Char ? (value & 0xFF) : value;
        }

        public string ReadArrayAsString()
        {
            if (_array is null) throw new MiniCRuntimeException("Variable is not an array");
            var sb = new StringBuilder();
            foreach (var cell in _array)
            {
                if (cell == 0) break;
                sb.Append((char)cell);
            }
            return sb.ToString();
        }

        public void WriteStringToArray(string value)
        {
            if (_array is null) throw new MiniCRuntimeException("Variable is not an array");
            var len = Math.Min(value.Length, _array.Length > 0 ? _array.Length - 1 : 0);
            int i = 0;
            for (; i < len; i++) _array[i] = value[i];
            if (_array.Length > 0) _array[len] = 0;
            for (int j = len + 1; j < _array.Length; j++) _array[j] = 0;
        }
    }

    public readonly struct MiniCLValue
    {
        private readonly MiniCVariable _variable;
        private readonly int? _index;

        public MiniCLValue(MiniCVariable variable, int? index)
        {
            _variable = variable;
            _index = index;
        }

        public MiniCValue Get()
        {
            if (_index.HasValue)
                return MiniCValue.FromInt(_variable.GetArrayElement(_index.Value));
            return _variable.GetValue();
        }

        public void Set(MiniCValue value)
        {
            if (_index.HasValue)
            {
                _variable.SetArrayElement(_index.Value, value.AsInt());
            }
            else
            {
                _variable.SetValue(value);
            }
        }
    }

    public readonly struct MiniCValue
    {
        public MiniCValueKind Kind { get; }
        private readonly int _intValue;
        private readonly string? _stringValue;

        private MiniCValue(MiniCValueKind kind, int intValue, string? stringValue)
        {
            Kind = kind;
            _intValue = intValue;
            _stringValue = stringValue;
        }

        public static MiniCValue FromInt(int value) => new(MiniCValueKind.Int, value, null);
        public static MiniCValue FromString(string value) => new(MiniCValueKind.String, 0, value);
        public static MiniCValue Void => new(MiniCValueKind.Void, 0, null);

        public int AsInt()
        {
            if (Kind == MiniCValueKind.Int) return _intValue;
            if (Kind == MiniCValueKind.Void) return 0;
            throw new MiniCRuntimeException("Expected integer value");
        }

        public string AsString()
        {
            return Kind switch
            {
                MiniCValueKind.String => _stringValue ?? string.Empty,
                MiniCValueKind.Int => _intValue.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }
    }

    public enum MiniCValueKind { Void, Int, String }

    public sealed class MiniCRuntimeException : Exception
    {
        public MiniCRuntimeException(string message) : base(message) { }
    }

    internal sealed class MiniCReturnSignal : Exception
    {
        public MiniCValue Value { get; }
        public MiniCReturnSignal(MiniCValue value) => Value = value;
    }

    internal sealed class MiniCBreakSignal : Exception
    {
        public static readonly MiniCBreakSignal Instance = new();
        private MiniCBreakSignal() { }
    }

    internal sealed class MiniCContinueSignal : Exception
    {
        public static readonly MiniCContinueSignal Instance = new();
        private MiniCContinueSignal() { }
    }

    internal static class MiniCBuiltins
    {
        private static readonly Dictionary<string, Func<MiniCEvalContext, IReadOnlyList<MiniCValue>, MiniCValue>> _builtins = new(StringComparer.Ordinal)
        {
            ["puts"] = Puts,
            ["putchar"] = PutChar,
            ["getchar"] = GetChar,
            ["printf"] = Printf,
            ["sleep_ms"] = Sleep,
            ["clock_ms"] = Clock,
            ["readall"] = ReadAll,
            ["writeall"] = WriteAll,
            ["readln"] = ReadLine,
            ["spawn"] = Spawn,
            ["wait"] = Wait
        };

        public static bool TryInvoke(string name, MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args, out MiniCValue value)
        {
            if (_builtins.TryGetValue(name, out var impl))
            {
                value = impl(ctx, args);
                return true;
            }
            value = MiniCValue.Void;
            return false;
        }

        private static MiniCValue Puts(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("puts expects 1 argument");
            ctx.Sys.PrintLine(args[0].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue PutChar(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("putchar expects 1 argument");
            ctx.Sys.Print(((char)args[0].AsInt()).ToString());
            return args[0];
        }

        private static MiniCValue GetChar(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("getchar expects no arguments");
            int ch = ctx.Sys.ReadChar();
            return MiniCValue.FromInt(ch);
        }

        private static MiniCValue Printf(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count == 0) throw new MiniCRuntimeException("printf requires format string");
            var fmt = args[0].AsString();
            var sb = new StringBuilder();
            int argIndex = 1;
            for (int i = 0; i < fmt.Length; i++)
            {
                var ch = fmt[i];
                if (ch == '%' && i + 1 < fmt.Length)
                {
                    var spec = fmt[++i];
                    if (spec == '%') { sb.Append('%'); continue; }
                    if (argIndex >= args.Count) throw new MiniCRuntimeException("printf missing arguments");
                    var arg = args[argIndex++];
                    switch (spec)
                    {
                        case 'd': sb.Append(arg.AsInt()); break;
                        case 'c': sb.Append((char)arg.AsInt()); break;
                        case 's': sb.Append(arg.AsString()); break;
                        case 'x': sb.Append(Convert.ToString(arg.AsInt(), 16)); break;
                        default:
                            sb.Append('%').Append(spec);
                            break;
                    }
                    continue;
                }
                sb.Append(ch);
            }
            ctx.Sys.Print(sb.ToString());
            return MiniCValue.FromInt(sb.Length);
        }

        private static MiniCValue Sleep(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("sleep_ms expects 1 argument");
            ctx.Sys.Sleep(args[0].AsInt(), ctx.CancellationToken);
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue Clock(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("clock_ms expects no args");
            return MiniCValue.FromInt(unchecked((int)ctx.Sys.TimeMilliseconds()));
        }

        private static MiniCValue ReadAll(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("readall expects path");
            var path = args[0].AsString();
            var text = ctx.Sys.ReadAllText(path);
            return MiniCValue.FromString(text);
        }

        private static MiniCValue WriteAll(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("writeall expects path + text");
            ctx.Sys.WriteAllText(args[0].AsString(), args[1].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue ReadLine(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("readln expects no args");
            return MiniCValue.FromString(ctx.Sys.ReadLine());
        }

        private static MiniCValue Spawn(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("spawn expects path");
            var pid = ctx.Sys.Spawn(args[0].AsString());
            return MiniCValue.FromInt(pid);
        }

        private static MiniCValue Wait(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("wait expects pid");
            var exit = ctx.Sys.Wait(args[0].AsInt());
            return MiniCValue.FromInt(exit);
        }
    }

}
