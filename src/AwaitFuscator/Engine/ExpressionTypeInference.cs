using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Echo.Ast;
using Echo.Code;

namespace AwaitFuscator.Engine;

public class ExpressionTypeInference : IAstNodeVisitor<CilInstruction, object?, TypeSignature?>
{
    private readonly Dictionary<IVariable, TypeSignature> _variableTypes = new();
    private readonly MethodDefinition _method;

    public ExpressionTypeInference(MethodDefinition method)
    {
        _method = method;
    }

    public void SetVariableType(IVariable variable, TypeSignature type) => _variableTypes[variable] = type;

    public TypeSignature? InferType(Expression<CilInstruction> expression) => expression.Accept(this, null);

    public TypeSignature? Visit(AssignmentStatement<CilInstruction> statement, object? state) => null;

    public TypeSignature? Visit(ExpressionStatement<CilInstruction> statement, object? state)
    {
        return statement.Expression.Accept(this, state);
    }

    public TypeSignature? Visit(PhiStatement<CilInstruction> statement, object? state) => null;

    public TypeSignature? Visit(InstructionExpression<CilInstruction> expression, object? state)
    {
        var argumentTypes = expression.Arguments
            .Select(x => x.Accept(this, state))
            .ToArray();

        switch (expression.Instruction.OpCode.Code)
        {
            case CilCode.Not:
            case CilCode.Neg:
            case CilCode.Dup:
            case CilCode.Or:
            case CilCode.And:
            case CilCode.Xor:
            case CilCode.Shl:
            case CilCode.Shr:
            case CilCode.Shr_Un:
                return argumentTypes[0];

            case CilCode.Ldstr:
                return _method.Module!.CorLibTypeFactory.String;

            case CilCode.Ceq:
            case CilCode.Cgt:
            case CilCode.Cgt_Un:
            case CilCode.Clt:
            case CilCode.Clt_Un:
                return _method.Module!.CorLibTypeFactory.Boolean;

            case CilCode.Add:
            case CilCode.Add_Ovf:
            case CilCode.Add_Ovf_Un:
            case CilCode.Sub:
            case CilCode.Sub_Ovf:
            case CilCode.Sub_Ovf_Un:
            case CilCode.Mul:
            case CilCode.Mul_Ovf:
            case CilCode.Mul_Ovf_Un:
            case CilCode.Div:
            case CilCode.Div_Un:
            case CilCode.Rem:
            case CilCode.Rem_Un:
                return ArithmeticOperator(argumentTypes[0], argumentTypes[1]);

            case CilCode.Ldc_I4:
            case CilCode.Ldc_I4_0:
            case CilCode.Ldc_I4_1:
            case CilCode.Ldc_I4_2:
            case CilCode.Ldc_I4_3:
            case CilCode.Ldc_I4_4:
            case CilCode.Ldc_I4_5:
            case CilCode.Ldc_I4_6:
            case CilCode.Ldc_I4_7:
            case CilCode.Ldc_I4_8:
            case CilCode.Ldc_I4_S:
            case CilCode.Ldc_I4_M1:
            case CilCode.Ldelem_I4:
            case CilCode.Ldind_I4:
            case CilCode.Conv_I4:
            case CilCode.Conv_Ovf_I4:
            case CilCode.Conv_Ovf_I4_Un:
                return _method.Module!.CorLibTypeFactory.Int32;

            case CilCode.Ldc_I8:
            case CilCode.Ldelem_I8:
            case CilCode.Ldind_I8:
            case CilCode.Conv_I8:
            case CilCode.Conv_Ovf_I8:
            case CilCode.Conv_Ovf_I8_Un:
                return _method.Module!.CorLibTypeFactory.Int64;

            case CilCode.Ldc_R4:
            case CilCode.Conv_R4:
            case CilCode.Ldind_R4:
                return _method.Module!.CorLibTypeFactory.Single;

            case CilCode.Ldc_R8:
            case CilCode.Ldind_R8:
            case CilCode.Conv_R8:
            case CilCode.Conv_R_Un:
                return _method.Module!.CorLibTypeFactory.Double;

            case CilCode.Ldelem_I1:
            case CilCode.Ldind_I1:
            case CilCode.Conv_I1:
            case CilCode.Conv_Ovf_I1:
            case CilCode.Conv_Ovf_I1_Un:
                return _method.Module!.CorLibTypeFactory.SByte;

            case CilCode.Ldelem_I2:
            case CilCode.Ldind_I2:
            case CilCode.Conv_I2:
            case CilCode.Conv_Ovf_I2:
            case CilCode.Conv_Ovf_I2_Un:
                return _method.Module!.CorLibTypeFactory.Int16;

            case CilCode.Ldelem_U1:
            case CilCode.Ldind_U1:
            case CilCode.Conv_U1:
            case CilCode.Conv_Ovf_U1:
            case CilCode.Conv_Ovf_U1_Un:
                return _method.Module!.CorLibTypeFactory.Byte;

            case CilCode.Ldelem_U2:
            case CilCode.Ldind_U2:
            case CilCode.Conv_U2:
            case CilCode.Conv_Ovf_U2:
            case CilCode.Conv_Ovf_U2_Un:
                return _method.Module!.CorLibTypeFactory.UInt16;

            case CilCode.Ldelem_U4:
            case CilCode.Ldind_U4:
            case CilCode.Conv_U4:
            case CilCode.Conv_Ovf_U4:
            case CilCode.Conv_Ovf_U4_Un:
                return _method.Module!.CorLibTypeFactory.UInt32;

            case CilCode.Conv_U8:
            case CilCode.Conv_Ovf_U8:
            case CilCode.Conv_Ovf_U8_Un:
                return _method.Module!.CorLibTypeFactory.UInt64;

            case CilCode.Ldelem_I:
            case CilCode.Ldind_I:
            case CilCode.Conv_I:
            case CilCode.Conv_Ovf_I:
            case CilCode.Conv_Ovf_I_Un:
                return _method.Module!.CorLibTypeFactory.IntPtr;

            case CilCode.Conv_U:
            case CilCode.Conv_Ovf_U:
            case CilCode.Conv_Ovf_U_Un:
                return _method.Module!.CorLibTypeFactory.UIntPtr;

            case CilCode.Ldarg:
            case CilCode.Ldarg_0:
            case CilCode.Ldarg_1:
            case CilCode.Ldarg_2:
            case CilCode.Ldarg_3:
            case CilCode.Ldarg_S:
                return expression.Instruction.GetParameter(_method.Parameters).ParameterType;

            case CilCode.Ldloc:
            case CilCode.Ldloc_0:
            case CilCode.Ldloc_1:
            case CilCode.Ldloc_2:
            case CilCode.Ldloc_3:
            case CilCode.Ldloc_S:
                return expression.Instruction.GetLocalVariable(_method.CilMethodBody!.LocalVariables).VariableType;

            case CilCode.Ldfld:
            case CilCode.Ldsfld:
                return ((IFieldDescriptor) expression.Instruction.Operand!).Signature!.FieldType;

            case CilCode.Call:
            case CilCode.Callvirt:
                return ((IMethodDescriptor) expression.Instruction.Operand!).Signature!.ReturnType;

            case CilCode.Calli:
                return ((MethodSignature) ((StandAloneSignature) expression.Instruction.Operand!).Signature!).ReturnType;

            case CilCode.Newobj:
                return ((IMethodDescriptor) expression.Instruction.Operand!).DeclaringType!.ToTypeSignature();

            case CilCode.Newarr:
                return ((ITypeDefOrRef) expression.Instruction.Operand!).MakeSzArrayType();

            case CilCode.Box:
                return _method.Module!.CorLibTypeFactory.Object;

            case CilCode.Unbox_Any:
                return ((ITypeDefOrRef) expression.Instruction.Operand!).ToTypeSignature();

            default:
                return null;
        }
    }

    private TypeSignature? ArithmeticOperator(TypeSignature? a, TypeSignature? b)
    {
        if (a is null)
            return b;
        if (b is null)
            return a;

        var factory = _method.Module!.CorLibTypeFactory;

        return (ToCliType(a).ElementType, ToCliType(b).ElementType) switch
        {
            (ElementType.I4, ElementType.I4) => factory.Int32,
            (ElementType.I4, ElementType.I8) => factory.Int64,
            (ElementType.I8, ElementType.I4) => factory.Int64,
            (ElementType.I8, ElementType.I8) => factory.Int64,

            (ElementType.I, ElementType.I4) => factory.IntPtr,
            (ElementType.I, ElementType.I8) => factory.IntPtr,
            (ElementType.I4, ElementType.I) => factory.IntPtr,
            (ElementType.I8, ElementType.I) => factory.IntPtr,
            (ElementType.I, ElementType.I) => factory.IntPtr,

            (ElementType.R4, ElementType.R4) => factory.Single,
            (ElementType.R4, ElementType.R8) => factory.Double,
            (ElementType.R8, ElementType.R4) => factory.Double,
            (ElementType.R8, ElementType.R8) => factory.Double,

            _ => a
        };
    }

    private TypeSignature ToCliType(TypeSignature a)
    {
        var factory = _method.Module!.CorLibTypeFactory;

        switch (a.ElementType)
        {
            case ElementType.Boolean:
            case ElementType.Char:
            case ElementType.I1:
            case ElementType.I2:
            case ElementType.I4:
            case ElementType.U1:
            case ElementType.U2:
            case ElementType.U4:
                return factory.Int32;

            case ElementType.I8:
            case ElementType.U8:
                return factory.Int64;

            case ElementType.I:
            case ElementType.U:
                return factory.IntPtr;

            case ElementType.Class:
            case ElementType.Object:
                return factory.Object;

            default:
                return a;
        }
    }

    public TypeSignature? Visit(VariableExpression<CilInstruction> expression, object? state)
    {
        _variableTypes.TryGetValue(expression.Variable, out var type);
        return type;
    }
}