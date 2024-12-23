using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Echo.Ast;
using Echo.Code;
using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace AwaitFuscator.Engine;

/// <summary>
/// Provides a mechanism for constructing new MoveNext methods in an async state machine.
/// </summary>
public class MoveNextBuilder
{
    private readonly ObfuscatorContext _context;
    private readonly AsyncStateMachineType _stateMachineType;
    private readonly MethodDefinition _method;

    private readonly MethodDefinition _moveNext;
    private readonly CilMethodBody _body;
    private readonly CilInstructionCollection _il;
    private readonly CilInstructionLabel _tryStart = new();
    private readonly CilInstructionLabel _handlerEnd = new();
    private readonly CilInstructionLabel _normalReturn = new();
    private readonly CilInstructionLabel _return = new();
    private readonly List<ICilLabel> _switchLabels = new();
    private readonly CilLocalVariable _stateLocal;
    private readonly CilLocalVariable _conditionLocal;
    private readonly CilLocalVariable? _resultLocal;

    private readonly List<Awaiter> _awaiters = new();
    private TypeSignature? _currentInputType;

    private readonly Dictionary<Parameter, FieldDefinition> _mappedParameters = new();
    private readonly Dictionary<CilLocalVariable, FieldDefinition> _mappedLocals = new();
    private readonly Dictionary<IVariable, FieldDefinition> _syntheticVariables = new();
    private readonly FieldDefinition _frameLocalField;
    private readonly FieldDefinition _conditionLocalField;
    private readonly TypeSignature _frameTypeSignature;

    private readonly Dictionary<int, CilInstructionLabel> _mappedLabels = new();

    private readonly AsyncMethodBuilderFactory _methodBuilderFactory;
    private readonly ExpressionTypeInference _typeInference;

    public MoveNextBuilder(ObfuscatorContext context, AsyncStateMachineType stateMachineType, MethodDefinition method)
    {
        _context = context;
        _stateMachineType = stateMachineType;
        _method = method;
        _methodBuilderFactory = context.CodeFactory.GetMethodBuilderFactory(method.Signature!.ReturnType);
        _typeInference = new ExpressionTypeInference(_method);

        var factory = _context.TargetModule.CorLibTypeFactory;

        // Create MoveNext definition.
        _moveNext = new MethodDefinition(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            MethodSignature.CreateInstance(factory.Void)
        );

        _body = _moveNext.CilMethodBody = new CilMethodBody(_moveNext);

        // Define important local variables.
        _body.LocalVariables.Add(_stateLocal = new CilLocalVariable(factory.Int32));
        _body.LocalVariables.Add(_conditionLocal = new CilLocalVariable(factory.Boolean));
        if (method.Signature.ReturnsValue)
            _body.LocalVariables.Add(_resultLocal = new CilLocalVariable(method.Signature.ReturnType));

        _il = _body.Instructions;

        // Construct Frame Type.
        FrameType = new TypeDefinition(
            null,
            context.Parameters.UseAnonymousTypes
                ? $"<>AnonType_{method.MetadataToken.Rid}_Frame"
                : $"Frame_{method.MetadataToken}",
            TypeAttributes.Sealed,
            context.TargetModule.CorLibTypeFactory.Object.Type
        );
        FrameType.CustomAttributes.Add(new CustomAttribute(context.CodeFactory.CompilerGeneratedAttributeConstructor));

        // Define parameters as fields.
        foreach (var (parameter, parameterField) in stateMachineType.ParameterFields)
        {
            var frameField = new FieldDefinition(
                parameter.Name,
                FieldAttributes.Public,
                parameterField.Signature!.FieldType
            );

            FrameType.Fields.Add(frameField);
            _mappedParameters[parameter] = frameField;
        }

        // Define locals as fields.
        foreach (var local in method.CilMethodBody!.LocalVariables)
        {
            var frameField = new FieldDefinition(
                $"_local{local.Index}",
                FieldAttributes.Public,
                local.VariableType
            );

            FrameType.Fields.Add(frameField);
            _mappedLocals[local] = frameField;
        }

        // Define the frame as a local field.
        _frameLocalField = new FieldDefinition(
            $"<x>5__1",
            FieldAttributes.Public,
            context.Parameters.UseAnonymousTypes
                ? factory.Object
                : FrameType.ToTypeSignature()
        );

        // We need an extra field to store the result of a condition of a branch.
        _conditionLocalField = new FieldDefinition("<c>5__2",FieldAttributes.Public, factory.Boolean);

        // Add a default constructor.
        FrameType.Methods.Add(MethodDefinition.CreateConstructor(_context.TargetModule));

        _frameTypeSignature = FrameType.ToTypeSignature(false);
    }

    /// <summary>
    /// Gets the type containing all fields representing the local variables in this method.
    /// </summary>
    public TypeDefinition FrameType { get; }

    /// <summary>
    /// Gets all the awaiter types the MoveNext method depends on and need to be added to the module.
    /// </summary>
    public IEnumerable<TypeDefinition> GetAwaiterTypes() => _awaiters.Select(x => x.Type.Definition);

    /// <summary>
    /// Gets all the awaiter fields the MoveNext method depends on and need to be added to the declaring state machine.
    /// </summary>
    public IEnumerable<FieldDefinition> GetAwaiterFields() => _awaiters.Select(x => x.Field);

    /// <summary>
    /// Gets all the local fields the MoveNext method directly depends on and need to be added to the declaring state
    /// machine.
    /// </summary>
    public IEnumerable<FieldDefinition> GetLocalFields() => new[] { _frameLocalField, _conditionLocalField };

    /// <summary>
    /// Gets all the GetAwaiter methods the MoveNext method depends on and need to be added to the module.
    /// </summary>
    public IEnumerable<MethodDefinition> GetGetAwaiterMethods() => _awaiters.Select(x => x.GetAwaiter);

    /// <summary>
    /// Gets the field representing the provided local variable.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <returns>The field.</returns>
    public FieldDefinition GetLocalField(CilLocalVariable local) => _mappedLocals[local];

    /// <summary>
    /// Gets the field representing the provided parameter.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The field.</returns>
    public FieldDefinition GetParameterField(Parameter parameter) => _mappedParameters[parameter];

    public FieldDefinition GetSyntheticField(IVariable variable)
    {
        if (!_syntheticVariables.TryGetValue(variable, out var field))
        {
            field = new FieldDefinition($"<>s__{_syntheticVariables.Count}", FieldAttributes.Public,
                _context.TargetModule.CorLibTypeFactory.Object);
            _syntheticVariables.Add(variable, field);
            FrameType.Fields.Add(field);
        }

        return field;
    }

    /// <summary>
    /// Starts a new MoveNext method.
    /// </summary>
    public void Begin()
    {
        // state = <>1__state;
        _il.Add(Ldarg_0);
        _il.Add(Ldfld, _stateMachineType.StateField);
        _il.Add(Stloc, _stateLocal);

        // try
        // {
        //      switch (state)
        _tryStart.Instruction = _il.Add(Ldloc, _stateLocal);
        _il.Add(new CilInstruction(Switch, _switchLabels));

        //      default:
        //          frame = new Frame();
        _il.Add(Ldarg_0);
        _il.Add(Newobj, FrameType.GetConstructor()!);

        if (_mappedParameters.Count > 0)
        {
            foreach (var parameter in _mappedParameters)
            {
                // frame.Parameter = this.Parameter;
                _il.Add(Dup);
                _il.Add(Ldarg_0);
                _il.Add(Ldfld, _stateMachineType.ParameterFields[parameter.Key]);
                _il.Add(Stfld, _mappedParameters[parameter.Key]);
            }
        }

        _il.Add(Stfld, _frameLocalField);
    }

    /// <summary>
    /// Finalizes the MoveNext code generation.
    /// </summary>
    /// <returns>The finalized MoveNext method.</returns>
    public MethodDefinition End()
    {
        var ex = new CilLocalVariable(_context.CodeFactory.ExceptionType.ToTypeSignature(false));
        _body.LocalVariables.Add(ex);

        _normalReturn.Instruction = _il.Add(Leave, _handlerEnd);

        // }
        // catch (Exception ex)
        // {
        var handlerStart = _il.Add(Stloc, ex).CreateLabel();

        //      <>1__state = -2;
        _il.Add(Ldarg_0);
        _il.Add(Ldc_I4, -2);
        _il.Add(Stfld, _stateMachineType.StateField);

        //      <>t__builder.SetException(ex);
        _il.Add(Ldarg_0);
        _il.Add(Ldflda, _stateMachineType.BuilderField);
        _il.Add(Ldloc, ex);
        _il.Add(Call, _methodBuilderFactory.SetExceptionMethod);

        //      return;
        _il.Add(Nop);
        _il.Add(Leave, _return);

        // }

        // <>1__state = -2;
        _handlerEnd.Instruction = _il.Add(Ldarg_0);
        _il.Add(Ldc_I4, -2);
        _il.Add(Stfld, _stateMachineType.StateField);

        // <>t__builder.SetResult([result]);
        _il.Add(Ldarg_0);
        _il.Add(Ldflda, _stateMachineType.BuilderField);
        if (_resultLocal is not null)
            _il.Add(Ldloc, _resultLocal);
        _il.Add(Call, _methodBuilderFactory.SetResult);
        _il.Add(Nop);

        // return;
        _return.Instruction = _il.Add(Ret);

        _body.ExceptionHandlers.Add(new CilExceptionHandler
        {
            HandlerType = CilExceptionHandlerType.Exception,
            TryStart = _tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = _handlerEnd,
            ExceptionType = _context.CodeFactory.ExceptionType
        });

        _il.OptimizeMacros();

        return _moveNext;
    }

    /// <summary>
    /// Registers all basic block labels of the original method.
    /// </summary>
    /// <param name="offsets">The offsets of all basic blocks.</param>
    public void RegisterLabels(IEnumerable<int> offsets)
    {
        foreach (int offset in offsets)
            _mappedLabels[offset] = new CilInstructionLabel();
    }

    /// <summary>
    /// Starts the code generation of a new basic block.
    /// </summary>
    /// <param name="offset">The original offset of the basic block.</param>
    public void BeginBlock(int offset)
    {
        _currentInputType = _context.TargetModule.CorLibTypeFactory.Object;
        AppendSequential(new CilInstruction(offset, Ldarg_0));
        _il.Add(Ldfld, _frameLocalField);
    }

    /// <summary>
    /// Ends the code generation of the current basic block.
    /// </summary>
    public void EndBlock()
    {
        EnsureCurrentAwaiterPopped(-1);
    }

    private void EnsureCurrentAwaiterPopped(int offset)
    {
        if (_currentInputType is not null)
        {
            AppendSequential(new CilInstruction(offset, Pop));
            _currentInputType = null;
        }
    }

    /// <summary>
    /// Appends a statement to the current block.
    /// </summary>
    /// <param name="statement">The statement to append.</param>
    /// <exception cref="InvalidOperationException">Occurs when no block was started yet.</exception>
    public void AppendStatement(Statement<CilInstruction> statement)
    {
        if (_currentInputType is null)
            throw new InvalidOperationException();

        switch (statement)
        {
            // Special-case on some types of instructions that alter flow or are redundant.
            case ExpressionStatement<CilInstruction>
            {
                Expression: InstructionExpression<CilInstruction> {Instruction: { } instruction} expression
            } when instruction.IsUnconditionalBranch():
                AppendUnconditionalBranch(expression);
                return;

            case ExpressionStatement<CilInstruction>
            {
                Expression: InstructionExpression<CilInstruction> {Instruction: { } instruction} expression
            } when instruction.IsConditionalBranch():
                AppendConditionalBranch(expression);
                return;

            case ExpressionStatement<CilInstruction>
            {
                Expression: InstructionExpression<CilInstruction> {Instruction.OpCode.Code: var code} expression
            }:
                switch (code)
                {
                    case CilCode.Nop:
                        // Don't create an awaiter for nops (drastically reduces the amount of NOP awaiters).
                        AppendSequential(expression.Instruction);
                        return;

                    case CilCode.Ret:
                        AppendReturn(expression);
                        return;
                }

                break;
        }

        // Otherwise just append a normal awaiter construction.
        AppendAwaitedSequential(statement);
    }

    private void AppendSequential(CilInstruction instruction)
    {
        // Copy to prevent mutability bugs.
        var copy = new CilInstruction(instruction.OpCode, instruction.Operand);

        // Emit to the state.
        _il.Add(copy);

        // Is this the start of a basic block? Register it if necessary.
        if (_mappedLabels.TryGetValue(instruction.Offset, out var label))
            label.Instruction ??= copy;
    }

    private void AppendAwaitedSequential(AstNode<CilInstruction> statement)
    {
        // Create the next awaiter.
        var awaiter = CreateNextAwaiter(statement, null);

        // Copy the statement into the GetResult of the awaiter.
        var getResultIl = awaiter.Type.GetResultMethod.CilMethodBody!.Instructions;
        var serializer = new StatementSerializer(this, awaiter.Type);
        AstNodeWalker<CilInstruction>.Walk(serializer, statement);

        // Append 'return this' to GetResult().
        getResultIl.Add(Ldarg_0);
        getResultIl.Add(Ldobj, awaiter.Type.Definition);
        getResultIl.Add(Ret);

        // Append normal awaiter pattern to MoveNext.
        EmitAwaiterStatements(awaiter);

        _currentInputType = awaiter.Type.Definition.ToTypeSignature(true);
    }

    private void AppendReturn(InstructionExpression<CilInstruction> expression)
    {
        if (_resultLocal is not null && expression.Arguments.Count == 1)
        {
            // We are exiting the method with a value as a result. Create a new awaiter for it.
            var awaiter = CreateNextAwaiter(expression, _resultLocal.VariableType);

            // Emit expression to the GetResult of the awaiter.
            var serializer = new StatementSerializer(this, awaiter.Type);
            AstNodeWalker<CilInstruction>.Walk(serializer, expression.Arguments[0]);
            awaiter.Type.GetResultMethod.CilMethodBody!.Instructions.Add(Ret);

            // Append normal awaiter pattern.
            EmitAwaiterStatements(awaiter);

            // Store result in result variable.
            _il.Add(Stloc, _resultLocal);
        }
        else
        {
            // We're exiting the method without a value, ensure stack balance.
            EnsureCurrentAwaiterPopped(expression.Instruction.Offset);
        }

        // Rets have to be replaced with a branch to the end of the state machine.
        AppendSequential(new CilInstruction(expression.Instruction.Offset, Br, _normalReturn));
    }

    private void AppendUnconditionalBranch(InstructionExpression<CilInstruction> branch)
    {
        int offset = branch.Instruction.Offset;

        // We're exiting the block, ensure stack balance before jumping.
        EnsureCurrentAwaiterPopped(offset);

        // Map the branch target to the label in MoveNext.
        AppendSequential(new CilInstruction(offset, Br, _mappedLabels[((ICilLabel) branch.Instruction.Operand!).Offset]));

        _currentInputType = null;
    }

    private void AppendConditionalBranch(InstructionExpression<CilInstruction> branch)
    {
        // Create the next awaiter.
        var awaiter = CreateNextAwaiter(branch, _context.TargetModule.CorLibTypeFactory.Boolean);

        // Copy the statement into the GetResult of the awaiter.
        var getResultBody = awaiter.Type.GetResultMethod.CilMethodBody!;
        var getResultIl = getResultBody.Instructions;
        var serializer = new StatementSerializer(this, awaiter.Type);

        // We don't actually want to emit the branch to the awaiter, but instead let the awaiter return only the result
        // of the condition. Hence, we only compile the branch arguments.
        foreach (var argument in branch.Arguments)
            AstNodeWalker<CilInstruction>.Walk(serializer, argument);

        // Rewrite the branch opcode to a boolean expression.
        AppendBooleanBranchConditionExpression(branch.Instruction.OpCode, getResultIl);

        getResultIl.Add(Ret);

        // Append normal awaiter pattern.
        EmitAwaiterStatements(awaiter, new CilInstruction(Ldarg_0));

        // Store to intermediate stack field such that dnSpy properly interprets it as an 'await' put into a condition.
        // NOTE: This is a limitation of dnSpy's await decompiler. Latest ILSpy works fine without this.
        _il.Add(Stfld, _stateMachineType.ConditionField);
        _il.Add(Ldarg_0);
        _il.Add(Ldfld, _stateMachineType.ConditionField);
        _il.Add(Stloc, _conditionLocal);
        _il.Add(Ldloc, _conditionLocal);

        // Follow up with a generic brtrue branch, mapping the branch target to the representative label in MoveNext().
        _il.Add(Brtrue, _mappedLabels[((ICilLabel) branch.Instruction.Operand!).Offset]);

        _currentInputType = null;
    }

    private static void AppendBooleanBranchConditionExpression(CilOpCode opCode, CilInstructionCollection getResultIl)
    {
        switch (opCode.Code)
        {
            case CilCode.Brtrue:
            case CilCode.Brtrue_S:
                // We don't have to rewrite anything!
                break;

            case CilCode.Brfalse:
            case CilCode.Brfalse_S:
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            case CilCode.Beq:
            case CilCode.Beq_S:
                getResultIl.Add(Ceq);
                break;

            case CilCode.Bne_Un:
            case CilCode.Bne_Un_S:
                getResultIl.Add(Ceq);
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            case CilCode.Blt:
            case CilCode.Blt_S:
                getResultIl.Add(Clt);
                break;

            case CilCode.Blt_Un:
            case CilCode.Blt_Un_S:
                getResultIl.Add(Clt_Un);
                break;

            case CilCode.Bgt:
            case CilCode.Bgt_S:
                getResultIl.Add(Cgt);
                break;

            case CilCode.Bgt_Un:
            case CilCode.Bgt_Un_S:
                getResultIl.Add(Cgt_Un);
                break;

            case CilCode.Ble:
            case CilCode.Ble_S:
                getResultIl.Add(Cgt);
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            case CilCode.Ble_Un:
            case CilCode.Ble_Un_S:
                getResultIl.Add(Cgt_Un);
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            case CilCode.Bge:
            case CilCode.Bge_S:
                getResultIl.Add(Clt);
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            case CilCode.Bge_Un:
            case CilCode.Bge_Un_S:
                getResultIl.Add(Clt_Un);
                getResultIl.Add(Ldc_I4_0);
                getResultIl.Add(Ceq);
                break;

            default:
                throw new NotSupportedException($"Unsupported branch opcode {opCode}");
        }
    }

    private Awaiter CreateNextAwaiter(AstNode<CilInstruction> node, TypeSignature? outputType)
    {
        if (_currentInputType is null)
            throw new InvalidOperationException();

        int id = _awaiters.Count;

        long offset = node.OriginalRange!.Value.Start;
        var awaiterType = new AwaiterType(
            _context,
            _context.Parameters.UseAnonymousTypes
                ? $"<>AnonType_{_method.MetadataToken.Rid}_{offset:X}"
                : $"Awaiter_{_method.MetadataToken}_{offset:X}",
            _frameTypeSignature,
            _currentInputType,
            outputType
        );

        var awaiterTypeSig = awaiterType.Definition.ToTypeSignature(true);
        var awaiterField = new FieldDefinition($"<>u__{id + 1}", FieldAttributes.Private, awaiterTypeSig);
        var getAwaiterMethod = _context.CodeFactory.CreateGetAwaiterMethod(awaiterType, _currentInputType);

        var awaiter = new Awaiter(id, awaiterType, awaiterField, getAwaiterMethod);
        _awaiters.Add(awaiter);

        return awaiter;
    }

    private void EmitAwaiterStatements(Awaiter awaiter, CilInstruction? prepend = null)
    {
        var awaiterTypeSig = awaiter.Type.Definition.ToTypeSignature(true);

        // Awaiter awaiter;
        var awaiterLocal = new CilLocalVariable(awaiterTypeSig);
        _body.LocalVariables.Add(awaiterLocal);

        var nextStatement = new CilInstructionLabel();

        // awaiter = current.GetAwaiter();
        _il.Add(Call, awaiter.GetAwaiter);
        _il.Add(Stloc, awaiterLocal);

        // if (awaiter.IsCompleted) goto nextStatement;
        _il.Add(Ldloca, awaiterLocal);
        _il.Add(Call, awaiter.Type.GetIsCompletedMethod);
        _il.Add(Brtrue, nextStatement);

        // state = <>1__state = id;
        _il.Add(Ldarg_0);
        _il.Add(Ldc_I4, awaiter.StateId);
        _il.Add(Dup);
        _il.Add(Stloc, _stateLocal);
        _il.Add(Stfld, _stateMachineType.StateField);

        // <>u__x = awaiter;
        _il.Add(Ldarg_0);
        _il.Add(Ldloc, awaiterLocal);
        _il.Add(Stfld, awaiter.Field);

        // <>t__builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        _il.Add(Ldarg_0);
        _il.Add(Ldflda, _stateMachineType.BuilderField);
        _il.Add(Ldloca, awaiterLocal);
        _il.Add(Ldarg_0);
        _il.Add(Call, _methodBuilderFactory.AwaitOnCompleted
            .MakeGenericInstanceMethod(
                awaiterTypeSig,
                _stateMachineType.Definition.ToTypeSignature(true)
            ));

        // return;
        _il.Add(Leave, _return);

        // awaiter = <>u__x;
        var initLabel = _il.Add(Ldarg_0).CreateLabel();
        _il.Add(Ldfld, awaiter.Field);
        _il.Add(Stloc, awaiterLocal);
        _switchLabels.Add(initLabel);

        // <>u__x = default(Awaiter);
        _il.Add(Ldarg_0);
        _il.Add(Ldflda, awaiter.Field);
        _il.Add(Initobj, awaiter.Type.Definition);

        // state = <>1__state = -1;
        _il.Add(Ldarg_0);
        _il.Add(Ldc_I4_M1);
        _il.Add(Dup);
        _il.Add(Stloc, _stateLocal);
        _il.Add(Stfld, _stateMachineType.StateField);

        // awaiter.GetResult();
        if (prepend is not null)
        {
            // HACK: sometimes we need to prepend the instruction for stack balance. We should probably tidy this up
            //       into something more clean...
            nextStatement.Instruction = prepend;
            _il.Add(prepend);
            _il.Add(Ldloca, awaiterLocal);
        }
        else
        {
            nextStatement.Instruction = _il.Add(Ldloca, awaiterLocal);
        }

        _il.Add(Call, awaiter.Type.GetResultMethod);
    }

    private sealed record Awaiter(int StateId, AwaiterType Type, FieldDefinition Field, MethodDefinition GetAwaiter);

    private sealed class StatementSerializer : AstNodeListener<CilInstruction>
    {
        private readonly MoveNextBuilder _builder;
        private readonly AwaiterType _awaiterType;

        public StatementSerializer(MoveNextBuilder builder, AwaiterType awaiterType)
        {
            _builder = builder;
            _awaiterType = awaiterType;
        }

        public override void EnterInstructionExpression(InstructionExpression<CilInstruction> expression)
        {
            // If the beginning of this expression is supposed to be a branch label, register it.
            if (_builder._mappedLabels.ContainsKey((int) expression.OriginalRange!.Value.Start))
                _builder.AppendSequential(new CilInstruction((int) expression.OriginalRange.Value.Start, Nop));

            var instruction = expression.Instruction;

            if (instruction.IsLdloc()
                || instruction.IsStloc()
                || instruction.IsLdarg()
                || instruction.IsStarg()
                || instruction.OpCode.Code is CilCode.Ldloca or CilCode.Ldloca_S or CilCode.Ldarga or CilCode.Ldarga_S)
            {
                // We need to prepend locals with a push to the current frame so that we can access its fields.
                var il = _awaiterType.GetResultMethod.CilMethodBody!.Instructions;

                il.Add(Ldarg_0);
                il.Add(Ldfld, _awaiterType.FrameField);
            }

            base.EnterInstructionExpression(expression);
        }

        public override void ExitInstructionExpression(InstructionExpression<CilInstruction> expression)
        {
            var opCode = expression.Instruction.OpCode;
            object? operand = expression.Instruction.Operand;

            // Translate local accesses to field accesses in the current frame.
            if (expression.Instruction.IsLdloc())
            {
                opCode = Ldfld;
                operand = _builder.GetLocalField(expression.Instruction.GetLocalVariable(_builder._method.CilMethodBody!.LocalVariables));
            }
            else if (expression.Instruction.OpCode.Code is CilCode.Ldloca or CilCode.Ldloca_S)
            {
                opCode = Ldflda;
                operand = _builder.GetLocalField(expression.Instruction.GetLocalVariable(_builder._method.CilMethodBody!.LocalVariables));
            }
            else if (expression.Instruction.IsStloc())
            {
                opCode = Stfld;
                operand = _builder.GetLocalField(expression.Instruction.GetLocalVariable(_builder._method.CilMethodBody!.LocalVariables));
            }
            else if (expression.Instruction.IsLdarg())
            {
                opCode = Ldfld;
                operand = _builder.GetParameterField(expression.Instruction.GetParameter(_builder._method.Parameters));
            }
            else if (expression.Instruction.OpCode.Code is CilCode.Ldarga or CilCode.Ldarga_S)
            {
                opCode = Ldflda;
                operand = _builder.GetParameterField(expression.Instruction.GetParameter(_builder._method.Parameters));
            }
            else if (expression.Instruction.IsStarg())
            {
                opCode = Stfld;
                operand = _builder.GetParameterField(expression.Instruction.GetParameter(_builder._method.Parameters));
            }
            else if (expression.Instruction.IsBranch())
            {
                throw new ArgumentException("Cannot move branch instructions into an awaiter type.");
            }
            else
            {
                // If we accessed private members, we need to make the awaiter nested such that it can access those members.
                switch (expression.Instruction.Operand)
                {
                    case MethodDefinition method when !method.IsAccessibleFromType(_awaiterType.Definition):
                    case FieldDefinition field when !field.IsAccessibleFromType(_awaiterType.Definition):
                    case TypeDefinition type when !type.IsAccessibleFromType(_awaiterType.Definition):
                    case MemberReference reference when !reference.Resolve()?.IsAccessibleFromType(_awaiterType.Definition) ?? false:
                        _awaiterType.Definition.IsNestedAssembly = true;
                        break;
                }
            }

            _awaiterType.GetResultMethod.CilMethodBody!.Instructions.Add(new CilInstruction(opCode, operand));

            base.ExitInstructionExpression(expression);
        }

        public override void ExitVariableExpression(VariableExpression<CilInstruction> expression)
        {
            base.ExitVariableExpression(expression);

            var il = _awaiterType.GetResultMethod.CilMethodBody!.Instructions;

            il.Add(Ldarg_0);
            il.Add(Ldfld, _builder._frameLocalField);
            il.Add(Ldfld, _builder.GetSyntheticField(expression.Variable));
        }

        public override void EnterAssignmentStatement(AssignmentStatement<CilInstruction> statement)
        {
            base.EnterAssignmentStatement(statement);

            var il = _awaiterType.GetResultMethod.CilMethodBody!.Instructions;

            foreach (var _ in statement.Variables)
            {
                il.Add(Ldarg_0);
                il.Add(Ldfld, _awaiterType.FrameField);
            }
        }

        public override void ExitAssignmentStatement(AssignmentStatement<CilInstruction> statement)
        {
            base.ExitAssignmentStatement(statement);

            var type = _builder._typeInference.InferType(statement.Expression);

            foreach (var variable in statement.Variables)
            {
                var field = _builder.GetSyntheticField(variable);
                if (type is not null)
                {
                    field.Signature!.FieldType = type;
                    _builder._typeInference.SetVariableType(variable, type);
                }

                _awaiterType.GetResultMethod.CilMethodBody!.Instructions.Add(Stfld, field);
            }
        }

        public override void ExitPhiStatement(PhiStatement<CilInstruction> statement)
        {
            throw new NotSupportedException("Phi nodes are not supported yet.");
        }
    }
}