using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public sealed class MiniCRuntime
    {
        private readonly MiniCProgram _program;
        private readonly ISysApi _sys;
        private readonly MiniCMemory _memory;

        public MiniCRuntime(MiniCProgram program, ISysApi sys, MiniCMemory? memory = null)
        {
            _program = program;
            _sys = sys;
            _memory = memory ?? new MiniCMemory();
        }

        public Task<int> RunAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

        public int Run(CancellationToken ct)
        {
            var ctx = new MiniCEvalContext(_program, _sys, _memory, ct);
            var result = ctx.CallFunction("main", Array.Empty<MiniCValue>());
            return result.Kind == MiniCValueKind.Int ? result.AsInt() : 0;
        }
    }

    public sealed class MiniCEvalContext
    {
        private readonly MiniCProgram _program;
        private readonly ISysApi _sys;
        private readonly MiniCMemory _memory;
        private readonly CancellationToken _ct;
        private readonly Stack<MiniCStackFrame> _frames = new();
        private readonly Dictionary<string, MiniCVariable> _globals = new(StringComparer.Ordinal);

        public MiniCEvalContext(MiniCProgram program, ISysApi sys, MiniCMemory memory, CancellationToken ct)
        {
            _program = program;
            _sys = sys;
            _memory = memory;
            _ct = ct;
            InitializeGlobals();
        }

        public ISysApi Sys => _sys;
        public MiniCMemory Memory => _memory;
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
            if (fn.ReturnType.Kind == MiniCTypeKind.Void && !fn.ReturnType.IsPointer)
            {
                if (value.Kind != MiniCValueKind.Void)
                    throw new MiniCRuntimeException($"Function '{fn.Name}' returns void but value provided");
                return MiniCValue.Void;
            }
            if (value.Kind == MiniCValueKind.Void && !fn.ReturnType.IsPointer)
                return MiniCValue.FromInt(0);
            return NormalizeValueForType(fn.ReturnType, value);
        }

        public MiniCVariable ResolveVariable(string name)
        {
            if (_frames.TryPeek(out var frame) && frame.TryResolve(name, out var variable))
                return variable!;
            if (_globals.TryGetValue(name, out var global))
                return global;
            throw new MiniCRuntimeException($"Variable '{name}' not found");
        }

        private void InitializeGlobals()
        {
            foreach (var decl in _program.Globals)
            {
                if (_globals.ContainsKey(decl.Name))
                    throw new MiniCRuntimeException($"Global '{decl.Name}' already defined");
                var variable = new MiniCVariable(decl.Name, decl.Type, decl.IsArray, decl.ArrayLength);
                if (decl.IsArray)
                {
                    if (decl.Type.Kind == MiniCTypeKind.Char)
                    {
                        if (decl.Initializer is StringLiteralExpr literal)
                            variable.WriteStringToArray(literal.Value);
                        else if (decl.Initializer != null)
                        {
                            var value = EvaluateGlobalInitializer(decl.Initializer);
                            if (value.Kind != MiniCValueKind.String)
                                throw new MiniCRuntimeException("Char arrays require string literals");
                            variable.WriteStringToArray(value.AsString());
                        }
                    }
                    else if (decl.Initializer != null)
                    {
                        throw new MiniCRuntimeException("Only char arrays support string initializers");
                    }
                }
                else if (decl.Initializer != null)
                {
                    var value = EvaluateGlobalInitializer(decl.Initializer);
                    AssignValue(variable, value);
                }
                _globals[decl.Name] = variable;
            }
        }

        private static MiniCValue EvaluateGlobalInitializer(Expr expr) => expr switch
        {
            IntLiteralExpr ile => MiniCValue.FromInt(ile.Value),
            StringLiteralExpr sle => MiniCValue.FromString(sle.Value),
            _ => throw new MiniCRuntimeException("Global initializers must be literal values")
        };

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
        }

        internal MiniCValue NormalizeValueForType(MiniCVarType type, MiniCValue value)
        {
            if (type.IsPointer)
            {
                if (value.Kind == MiniCValueKind.Pointer)
                    return value;
                if (value.Kind == MiniCValueKind.String)
                    return MiniCValue.FromPointer(_memory.StoreString(value.AsString()));
                if (value.Kind == MiniCValueKind.Void || (value.Kind == MiniCValueKind.Int && value.AsInt() == 0))
                    return MiniCValue.NullPointer;
                throw new MiniCRuntimeException("Expected pointer value");
            }
            var numeric = value.Kind == MiniCValueKind.Void ? 0 : value.AsInt();
            return MiniCValue.FromInt(numeric);
        }

        internal void AssignValue(MiniCVariable variable, MiniCValue value)
        {
            if (variable.IsArray)
                throw new MiniCRuntimeException("Cannot assign entire array");
            var normalized = NormalizeValueForType(variable.Type, value);
            variable.SetValue(normalized);
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

        public bool TryResolve(string name, [MaybeNullWhen(false)] out MiniCVariable? variable)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out variable))
                    return true;
            }
            variable = null;
            return false;
        }

        public MiniCVariable Resolve(string name)
        {
            if (TryResolve(name, out var variable))
                return variable!;
            throw new MiniCRuntimeException($"Unknown identifier '{name}'");
        }
    }

    public sealed class MiniCVariable
    {
        private readonly MiniCValue[]? _array;
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
                if (!type.IsNumeric && !type.IsPointer)
                    throw new MiniCRuntimeException("Only numeric or pointer arrays supported");
                ArrayLength = arrayLength;
                _array = new MiniCValue[arrayLength];
                for (int i = 0; i < arrayLength; i++)
                    _array[i] = DefaultValue();
            }
            else
            {
                ArrayLength = 0;
                _value = DefaultValue();
            }
        }

        private MiniCValue DefaultValue()
        {
            if (Type.IsPointer)
                return MiniCValue.NullPointer;
            return MiniCValue.FromInt(0);
        }

        public MiniCValue GetValue()
        {
            if (IsArray) throw new MiniCRuntimeException("Array cannot be used as scalar value");
            return _value;
        }

        public void SetValue(MiniCValue value)
        {
            if (IsArray) throw new MiniCRuntimeException("Array cannot be assigned directly");
            _value = value;
        }

        public MiniCValue GetArrayElementValue(int index)
        {
            EnsureArrayIndex(index);
            return _array![index];
        }

        public void SetArrayElementValue(int index, MiniCValue value)
        {
            EnsureArrayIndex(index);
            if (Type.Kind == MiniCTypeKind.Char)
            {
                _array![index] = MiniCValue.FromInt(value.AsInt() & 0xFF);
                return;
            }
            if (Type.Kind == MiniCTypeKind.Int)
            {
                _array![index] = MiniCValue.FromInt(value.AsInt());
                return;
            }
            if (Type.Kind == MiniCTypeKind.Pointer)
            {
                if (value.Kind != MiniCValueKind.Pointer)
                    throw new MiniCRuntimeException("Expected pointer assignment");
                _array![index] = value;
                return;
            }
            _array![index] = value;
        }

        public string ReadArrayAsString()
        {
            if (_array is null || Type.Kind != MiniCTypeKind.Char)
                throw new MiniCRuntimeException("Variable is not a char array");
            var sb = new StringBuilder();
            foreach (var cell in _array)
            {
                var ch = cell.AsInt();
                if (ch == 0) break;
                sb.Append((char)(ch & 0xFF));
            }
            return sb.ToString();
        }

        public void WriteStringToArray(string value)
        {
            if (_array is null || Type.Kind != MiniCTypeKind.Char)
                throw new MiniCRuntimeException("Variable is not a char array");
            if (_array.Length == 0) return;
            var copyLen = Math.Min(value.Length, _array.Length - 1);
            int i = 0;
            for (; i < copyLen; i++) _array[i] = MiniCValue.FromInt(value[i]);
            _array[i] = MiniCValue.FromInt(0);
            for (int j = i + 1; j < _array.Length; j++) _array[j] = MiniCValue.FromInt(0);
        }

        private void EnsureArrayIndex(int index)
        {
            if (!IsArray) throw new MiniCRuntimeException("Variable is not an array");
            if ((uint)index >= (uint)ArrayLength)
                throw new MiniCRuntimeException($"Array index {index} out of range for '{Name}'");
        }
    }

    public readonly struct MiniCLValue
    {
        private readonly MiniCEvalContext _ctx;
        private readonly MiniCVariable? _variable;
        private readonly int? _index;
        private readonly MiniCPointer _pointer;
        private readonly MiniCLocationKind _kind;

        public MiniCLValue(MiniCEvalContext ctx, MiniCVariable variable, int? index)
        {
            _ctx = ctx;
            _variable = variable;
            _index = index;
            _pointer = MiniCPointer.Null;
            _kind = index.HasValue ? MiniCLocationKind.ArrayElement : MiniCLocationKind.Scalar;
        }

        public MiniCLValue(MiniCEvalContext ctx, MiniCPointer pointer)
        {
            _ctx = ctx;
            _variable = null;
            _index = null;
            _pointer = pointer;
            _kind = MiniCLocationKind.Memory;
        }

        public MiniCValue Get()
        {
            return _kind switch
            {
                MiniCLocationKind.Scalar => _variable!.GetValue(),
                MiniCLocationKind.ArrayElement => _variable!.GetArrayElementValue(_index!.Value),
                MiniCLocationKind.Memory => MiniCValue.FromInt(_pointer.Memory?.ReadByte(_pointer.Address) ?? 0),
                _ => MiniCValue.Void
            };
        }

        public void Set(MiniCValue value)
        {
            switch (_kind)
            {
                case MiniCLocationKind.Scalar:
                    _ctx.AssignValue(_variable!, value);
                    break;
                case MiniCLocationKind.ArrayElement:
                    var normalized = _ctx.NormalizeValueForType(_variable!.Type, value);
                    _variable.SetArrayElementValue(_index!.Value, normalized);
                    break;
                case MiniCLocationKind.Memory:
                    var b = (byte)(value.Kind == MiniCValueKind.Void ? 0 : value.AsInt() & 0xFF);
                    _pointer.Memory?.WriteByte(_pointer.Address, b);
                    break;
            }
        }
    }

    internal enum MiniCLocationKind { Scalar, ArrayElement, Memory }

    public readonly struct MiniCValue
    {
        public MiniCValueKind Kind { get; }
        private readonly int _intValue;
        private readonly string? _stringValue;
        private readonly MiniCPointer _pointerValue;

        private MiniCValue(MiniCValueKind kind, int intValue, string? stringValue, MiniCPointer pointerValue)
        {
            Kind = kind;
            _intValue = intValue;
            _stringValue = stringValue;
            _pointerValue = pointerValue;
        }

        public static MiniCValue FromInt(int value) => new(MiniCValueKind.Int, value, null, MiniCPointer.Null);
        public static MiniCValue FromString(string value) => new(MiniCValueKind.String, 0, value, MiniCPointer.Null);
        public static MiniCValue FromPointer(MiniCPointer pointer) => new(MiniCValueKind.Pointer, 0, null, pointer);
        public static MiniCValue NullPointer => FromPointer(MiniCPointer.Null);
        public static MiniCValue Void => new(MiniCValueKind.Void, 0, null, MiniCPointer.Null);

        public int AsInt()
        {
            return Kind switch
            {
                MiniCValueKind.Int => _intValue,
                MiniCValueKind.Void => 0,
                MiniCValueKind.Pointer => _pointerValue.IsNull ? 0 : _pointerValue.Address,
                _ => throw new MiniCRuntimeException("Expected integer value")
            };
        }

        public string AsString()
        {
            return Kind switch
            {
                MiniCValueKind.String => _stringValue ?? string.Empty,
                MiniCValueKind.Int => _intValue.ToString(CultureInfo.InvariantCulture),
                MiniCValueKind.Pointer => _pointerValue.Memory?.ReadString(_pointerValue) ?? string.Empty,
                MiniCValueKind.Void => string.Empty,
                _ => string.Empty
            };
        }

        public MiniCPointer AsPointer()
        {
            if (Kind == MiniCValueKind.Pointer) return _pointerValue;
            if (Kind == MiniCValueKind.Void) return MiniCPointer.Null;
            throw new MiniCRuntimeException("Expected pointer value");
        }

        public MiniCPointer AsPointer(MiniCMemory memory)
        {
            return Kind switch
            {
                MiniCValueKind.Pointer => _pointerValue,
                MiniCValueKind.String => memory.StoreString(_stringValue ?? string.Empty),
                MiniCValueKind.Void => MiniCPointer.Null,
                _ => throw new MiniCRuntimeException("Expected pointer value")
            };
        }
    }

    public enum MiniCValueKind { Void, Int, String, Pointer }

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
            ["cwd"] = Cwd,
            ["chdir"] = Chdir,
            ["malloc"] = Malloc,
            ["free"] = Free,
            ["memset"] = MemSet,
            ["memcpy"] = MemCopy,
            ["load32"] = Load32,
            ["store32"] = Store32,
            ["open"] = OpenFile,
            ["close"] = CloseFile,
            ["read"] = ReadFile,
            ["write"] = WriteFile,
            ["seek"] = SeekFile,
            ["stat"] = StatPath,
            ["opendir"] = OpenDir,
            ["readdir"] = ReadDir,
            ["rewinddir"] = RewindDir,
            ["dir_count"] = DirCount,
            ["dir_name"] = DirName,
            ["dir_is_dir"] = DirIsDir,
            ["dir_size"] = DirSize,
            ["mkdir"] = Mkdir,
            ["remove"] = Remove,
            ["unlink"] = Remove,
            ["isdir"] = IsDir,
            ["filesize"] = FileSize,
            ["sleep_ms"] = Sleep,
            ["clock_ms"] = Clock,
            ["readall"] = ReadAll,
            ["writeall"] = WriteAll,
            ["readln"] = ReadLine,
            ["spawn"] = Spawn,
            ["wait"] = Wait,
            ["proc_count"] = ProcCount,
            ["proc_pid"] = ProcPid,
            ["proc_name"] = ProcName,
            ["proc_state"] = ProcState,
            ["proc_kill"] = ProcKill,
            ["input"] = Input,
            ["rename"] = Rename,
            ["argc"] = ArgCount,
            ["argv"] = ArgValue,
            ["strlen"] = StrLen,
            ["strchar"] = StrChar,
            ["substr"] = SubStr,
            ["strcat"] = StrCat,
            ["startswith"] = StartsWith,
            ["exists"] = Exists
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

        private static MiniCValue Cwd(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("cwd expects no args");
            return MiniCValue.FromString(ctx.Sys.GetCwd());
        }

        private static MiniCValue Chdir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("chdir expects path");
            ctx.Sys.SetCwd(args[0].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue Malloc(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("malloc expects size");
            var size = Math.Max(0, args[0].AsInt());
            var ptr = ctx.Memory.Allocate(size);
            ctx.Memory.SetBytes(ptr.Address, 0, size);
            return MiniCValue.FromPointer(ptr);
        }

        private static MiniCValue Free(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("free expects pointer");
            var pointer = args[0].Kind == MiniCValueKind.Void ? MiniCPointer.Null : args[0].AsPointer();
            ctx.Memory.Free(pointer);
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue MemSet(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("memset expects pointer, value, count");
            var ptr = args[0].AsPointer();
            var value = (byte)(args[1].AsInt() & 0xFF);
            var count = Math.Max(0, args[2].AsInt());
            ctx.Memory.SetBytes(ptr.Address, value, count);
            return args[0];
        }

        private static MiniCValue MemCopy(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("memcpy expects dst, src, count");
            var dst = args[0].AsPointer();
            var src = args[1].AsPointer(ctx.Memory);
            var count = Math.Max(0, args[2].AsInt());
            ctx.Memory.Copy(dst, src, count);
            return args[0];
        }

        private static MiniCValue Load32(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("load32 expects pointer and offset");
            var ptr = args[0].AsPointer();
            var offset = args[1].AsInt();
            return MiniCValue.FromInt(ptr.Memory?.ReadInt32(ptr.Address + offset) ?? 0);
        }

        private static MiniCValue Store32(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("store32 expects pointer, offset, value");
            var ptr = args[0].AsPointer();
            var offset = args[1].AsInt();
            ptr.Memory?.WriteInt32(ptr.Address + offset, args[2].AsInt());
            return args[0];
        }

        private static FileOpenFlags DecodeFileFlags(int flags)
        {
            FileOpenFlags result = 0;
            if ((flags & 1) != 0) result |= FileOpenFlags.Read;
            if ((flags & 2) != 0) result |= FileOpenFlags.Write;
            if ((flags & 4) != 0) result |= FileOpenFlags.Create;
            if ((flags & 8) != 0) result |= FileOpenFlags.Truncate;
            if ((flags & 16) != 0) result |= FileOpenFlags.Append;
            if ((result & (FileOpenFlags.Read | FileOpenFlags.Write)) == 0)
                result |= FileOpenFlags.Read;
            return result;
        }

        private static SeekOrigin DecodeSeekOrigin(int value) => value switch
        {
            0 => SeekOrigin.Begin,
            2 => SeekOrigin.End,
            _ => SeekOrigin.Current
        };

        private static MiniCValue OpenFile(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count is < 1 or > 2) throw new MiniCRuntimeException("open expects path and optional flags");
            var path = args[0].AsString();
            var flags = args.Count == 2 ? args[1].AsInt() : 1;
            var fd = ctx.Sys.Open(path, DecodeFileFlags(flags));
            return MiniCValue.FromInt(fd);
        }

        private static MiniCValue CloseFile(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("close expects fd");
            return MiniCValue.FromInt(ctx.Sys.Close(args[0].AsInt()));
        }

        private static MiniCValue ReadFile(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("read expects fd, buffer, count");
            var fd = args[0].AsInt();
            var ptr = args[1].AsPointer();
            var count = Math.Max(0, args[2].AsInt());
            var buffer = new byte[count];
            var read = ctx.Sys.Read(fd, buffer);
            if (read > 0)
                ctx.Memory.WriteBytes(ptr.Address, buffer.AsSpan(0, read));
            return MiniCValue.FromInt(read);
        }

        private static MiniCValue WriteFile(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("write expects fd, buffer, count");
            var fd = args[0].AsInt();
            var ptr = args[1].AsPointer(ctx.Memory);
            var count = Math.Max(0, args[2].AsInt());
            var buffer = new byte[count];
            ctx.Memory.ReadBytes(ptr.Address, buffer.AsSpan());
            var written = ctx.Sys.Write(fd, buffer);
            return MiniCValue.FromInt(written);
        }

        private static MiniCValue SeekFile(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("seek expects fd, offset, origin");
            var fd = args[0].AsInt();
            var offset = args[1].AsInt();
            var origin = DecodeSeekOrigin(args[2].AsInt());
            return MiniCValue.FromInt(ctx.Sys.Seek(fd, offset, origin));
        }

        private static MiniCValue StatPath(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("stat expects path and buffer");
            var info = ctx.Sys.Stat(args[0].AsString());
            var ptr = args[1].AsPointer();
            var mem = ptr.Memory;
            if (mem is null) return MiniCValue.FromInt(-1);
            mem.WriteInt32(ptr.Address + 0, info.Exists ? 1 : 0);
            mem.WriteInt32(ptr.Address + 4, info.IsDir ? 1 : 0);
            mem.WriteInt32(ptr.Address + 8, (int)Math.Min(int.MaxValue, info.Size));
            return MiniCValue.FromInt(info.Exists ? 0 : -1);
        }

        private static MiniCValue OpenDir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("opendir expects path");
            return MiniCValue.FromInt(ctx.Sys.OpenDirectory(args[0].AsString()));
        }

        private static MiniCValue ReadDir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("readdir expects fd and buffer");
            var fd = args[0].AsInt();
            var ptr = args[1].AsPointer();
            if (!ctx.Sys.ReadDirectoryEntry(fd, out var entry))
                return MiniCValue.FromInt(0);
            var mem = ptr.Memory;
            if (mem is null) return MiniCValue.FromInt(-1);
            mem.WriteInt32(ptr.Address + 0, entry.IsDirectory ? 1 : 0);
            mem.WriteInt32(ptr.Address + 4, (int)Math.Min(int.MaxValue, entry.Size));
            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            mem.WriteBytes(ptr.Address + 8, nameBytes);
            mem.WriteByte(ptr.Address + 8 + nameBytes.Length, 0);
            return MiniCValue.FromInt(1);
        }

        private static MiniCValue RewindDir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("rewinddir expects fd");
            ctx.Sys.RewindDirectory(args[0].AsInt());
            return MiniCValue.FromInt(0);
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

        private static MiniCValue DirCount(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count > 1) throw new MiniCRuntimeException("dir_count expects optional path");
            var path = args.Count == 1 ? args[0].AsString() : ".";
            var count = ctx.Sys.ListEntries(path).Count();
            return MiniCValue.FromInt(count);
        }

        private static MiniCValue DirName(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("dir_name expects path and index");
            var entry = ResolveDirectoryEntry(ctx, args[0].AsString(), args[1].AsInt());
            return MiniCValue.FromString(entry.name);
        }

        private static MiniCValue DirIsDir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("dir_is_dir expects path and index");
            var entry = ResolveDirectoryEntry(ctx, args[0].AsString(), args[1].AsInt());
            return MiniCValue.FromInt(entry.isDir ? 1 : 0);
        }

        private static MiniCValue DirSize(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("dir_size expects path and index");
            var entry = ResolveDirectoryEntry(ctx, args[0].AsString(), args[1].AsInt());
            var size = entry.size > int.MaxValue ? int.MaxValue : (int)entry.size;
            return MiniCValue.FromInt(size);
        }

        private static MiniCValue Mkdir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("mkdir expects path");
            ctx.Sys.Mkdir(args[0].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue Remove(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("remove expects path");
            ctx.Sys.Remove(args[0].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue IsDir(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("isdir expects path");
            var info = ctx.Sys.Stat(args[0].AsString());
            return MiniCValue.FromInt(info.Exists && info.IsDir ? 1 : 0);
        }

        private static MiniCValue FileSize(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("filesize expects path");
            var info = ctx.Sys.Stat(args[0].AsString());
            if (!info.Exists) return MiniCValue.FromInt(-1);
            var size = info.Size > int.MaxValue ? int.MaxValue : (int)info.Size;
            return MiniCValue.FromInt(size);
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

        private static MiniCValue Input(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count > 1) throw new MiniCRuntimeException("input expects optional prompt");
            var prompt = args.Count == 1 ? args[0].AsString() : string.Empty;
            return MiniCValue.FromString(ctx.Sys.Input(prompt));
        }

        private static MiniCValue Rename(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("rename expects source and destination");
            ctx.Sys.Rename(args[0].AsString(), args[1].AsString());
            return MiniCValue.FromInt(0);
        }

        private static MiniCValue ProcCount(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("proc_count expects no args");
            return MiniCValue.FromInt(ctx.Sys.ListProcesses().Count());
        }

        private static MiniCValue ProcPid(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("proc_pid expects index");
            var proc = ResolveProcess(ctx, args[0].AsInt());
            return MiniCValue.FromInt(proc.Pid);
        }

        private static MiniCValue ProcName(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("proc_name expects index");
            var proc = ResolveProcess(ctx, args[0].AsInt());
            return MiniCValue.FromString(proc.Name);
        }

        private static MiniCValue ProcState(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("proc_state expects index");
            var proc = ResolveProcess(ctx, args[0].AsInt());
            return MiniCValue.FromString(proc.State.ToString());
        }

        private static MiniCValue ProcKill(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("proc_kill expects pid");
            var success = ctx.Sys.Kill(args[0].AsInt());
            return MiniCValue.FromInt(success ? 0 : -1);
        }

        private static MiniCValue ArgCount(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 0) throw new MiniCRuntimeException("argc expects no args");
            return MiniCValue.FromInt(ctx.Sys.ArgumentCount());
        }

        private static MiniCValue ArgValue(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("argv expects index");
            var index = args[0].AsInt();
            return MiniCValue.FromString(ctx.Sys.GetArgument(index));
        }

        private static MiniCValue StrLen(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("strlen expects 1 argument");
            return MiniCValue.FromInt(args[0].AsString().Length);
        }

        private static MiniCValue StrChar(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("strchar expects string and index");
            var text = args[0].AsString();
            var index = args[1].AsInt();
            if ((uint)index >= text.Length) return MiniCValue.FromInt(-1);
            return MiniCValue.FromInt(text[index]);
        }

        private static MiniCValue SubStr(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 3) throw new MiniCRuntimeException("substr expects string, start, length");
            var text = args[0].AsString();
            var start = Math.Max(0, args[1].AsInt());
            var length = Math.Max(0, args[2].AsInt());
            if (start >= text.Length || length == 0) return MiniCValue.FromString(string.Empty);
            var maxLen = Math.Min(length, text.Length - start);
            return MiniCValue.FromString(text.Substring(start, maxLen));
        }

        private static MiniCValue StrCat(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count < 2) throw new MiniCRuntimeException("strcat expects at least two arguments");
            var sb = new StringBuilder();
            foreach (var arg in args) sb.Append(arg.AsString());
            return MiniCValue.FromString(sb.ToString());
        }

        private static MiniCValue StartsWith(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 2) throw new MiniCRuntimeException("startswith expects value and prefix");
            var text = args[0].AsString();
            var prefix = args[1].AsString();
            return MiniCValue.FromInt(text.StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0);
        }

        private static MiniCValue Exists(MiniCEvalContext ctx, IReadOnlyList<MiniCValue> args)
        {
            if (args.Count != 1) throw new MiniCRuntimeException("exists expects path");
            return MiniCValue.FromInt(ctx.Sys.Exists(args[0].AsString()) ? 1 : 0);
        }

        private static (string name, bool isDir, long size) ResolveDirectoryEntry(MiniCEvalContext ctx, string path, int index)
        {
            var entries = ctx.Sys.ListEntries(path).ToList();
            if ((uint)index >= entries.Count)
                throw new MiniCRuntimeException("directory index out of range");
            return entries[index];
        }

        private static ProcessInfo ResolveProcess(MiniCEvalContext ctx, int index)
        {
            var processes = ctx.Sys.ListProcesses().ToList();
            if ((uint)index >= processes.Count)
                throw new MiniCRuntimeException("process index out of range");
            return processes[index];
        }
    }

}
