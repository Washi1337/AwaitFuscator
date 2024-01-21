using System.Runtime.CompilerServices;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

namespace AwaitFuscator.Engine;

/// <summary>
/// Provides members for easily constructing new references to various metadata related to <see cref="AsyncVoidMethodBuilder"/>
/// and <see cref="AsyncTaskMethodBuilder{TResult}"/>.
/// </summary>
public class AsyncMethodBuilderFactory
{
    public AsyncMethodBuilderFactory(
        ModuleDefinition targetModule,
        TypeSignature returnType,
        ITypeDefOrRef asyncStateMachineType,
        ITypeDefOrRef exceptionType)
    {
        var factory = targetModule.CorLibTypeFactory;

        if (returnType.ElementType == ElementType.Void)
        {
            Type = factory.CorLibScope
                .CreateTypeReference("System.Runtime.CompilerServices", "AsyncVoidMethodBuilder")
                .ImportWith(targetModule.DefaultImporter);

            CreateMethod = Type
                .CreateMemberReference("Create", MethodSignature.CreateStatic(Type.ToTypeSignature(true)))
                .ImportWith(targetModule.DefaultImporter);
        }
        else
        {
            Type = factory.CorLibScope
                .CreateTypeReference("System.Runtime.CompilerServices", "AsyncTaskMethodBuilder`1")
                .ToTypeSignature(true)
                .MakeGenericInstanceType(returnType)
                .ToTypeDefOrRef()
                .ImportWith(targetModule.DefaultImporter);

            CreateMethod = Type
                .CreateMemberReference(
                    "Create",
                    MethodSignature.CreateStatic(factory.CorLibScope
                        .CreateTypeReference("System.Runtime.CompilerServices", "AsyncTaskMethodBuilder`1")
                        .ToTypeSignature(true)
                        .MakeGenericInstanceType(new GenericParameterSignature(GenericParameterType.Type, 0)))
                ).ImportWith(targetModule.DefaultImporter);
        }

        StartMethod = Type
            .CreateMemberReference(
                "Start",
                MethodSignature.CreateInstance(
                    factory.Void, 1,
                    new GenericParameterSignature(GenericParameterType.Method, 0).MakeByReferenceType()
                )
            ).ImportWith(targetModule.DefaultImporter);

        SetStateMachineMethod = Type
            .CreateMemberReference(
                "SetStateMachine",
                MethodSignature.CreateInstance(
                    targetModule.CorLibTypeFactory.Void,
                    asyncStateMachineType.ToTypeSignature(false)
                )
            ).ImportWith(targetModule.DefaultImporter);

        SetExceptionMethod = Type
            .CreateMemberReference(
                "SetException",
                MethodSignature.CreateInstance(factory.Void, exceptionType.ToTypeSignature(false))
            ).ImportWith(targetModule.DefaultImporter);

        AwaitOnCompleted = Type
            .CreateMemberReference("AwaitOnCompleted", MethodSignature.CreateInstance(
                factory.Void, 2,
                new GenericParameterSignature(GenericParameterType.Method, 0).MakeByReferenceType(),
                new GenericParameterSignature(GenericParameterType.Method, 1).MakeByReferenceType()
            )).ImportWith(targetModule.DefaultImporter);

        if (returnType.ElementType == ElementType.Void)
        {
            SetResult = Type
                .CreateMemberReference("SetResult", MethodSignature.CreateInstance(factory.Void))
                .ImportWith(targetModule.DefaultImporter);

            TaskType = factory.CorLibScope
                .CreateTypeReference("System.Threading.Tasks", "Task")
                .ToTypeSignature(false)
                .ImportWith(targetModule.DefaultImporter);

            GetTaskMethod = Type
                .CreateMemberReference("get_Task", MethodSignature.CreateInstance(TaskType))
                .ImportWith(targetModule.DefaultImporter);

            TaskAwaiterType = factory.CorLibScope
                .CreateTypeReference("System.Runtime.CompilerServices", "TaskAwaiter")
                .ToTypeSignature(true)
                .ImportWith(targetModule.DefaultImporter);

            GetAwaiterMethod = TaskType
                .ToTypeDefOrRef()
                .CreateMemberReference("GetAwaiter", MethodSignature.CreateInstance(TaskAwaiterType))
                .ImportWith(targetModule.DefaultImporter);

            GetResultMethod = TaskAwaiterType
                .ToTypeDefOrRef()
                .CreateMemberReference("GetResult", MethodSignature.CreateInstance(factory.Void))
                .ImportWith(targetModule.DefaultImporter);
        }
        else
        {
            SetResult = Type
                .CreateMemberReference(
                    "SetResult",
                    MethodSignature.CreateInstance(
                        factory.Void,
                        new GenericParameterSignature(GenericParameterType.Type, 0))
                ).ImportWith(targetModule.DefaultImporter);

            TaskType = factory.CorLibScope
                .CreateTypeReference("System.Threading.Tasks", "Task`1")
                .ToTypeSignature(false)
                .MakeGenericInstanceType(returnType)
                .ImportWith(targetModule.DefaultImporter);

            GetTaskMethod = Type
                .CreateMemberReference(
                    "get_Task",
                    MethodSignature.CreateInstance(factory.CorLibScope
                        .CreateTypeReference("System.Threading.Tasks", "Task`1")
                        .ToTypeSignature(false)
                        .MakeGenericInstanceType(new GenericParameterSignature(GenericParameterType.Type, 0))
                    )
                ).ImportWith(targetModule.DefaultImporter);

            TaskAwaiterType = factory.CorLibScope
                .CreateTypeReference("System.Runtime.CompilerServices", "TaskAwaiter`1")
                .ToTypeSignature(true)
                .MakeGenericInstanceType(returnType)
                .ImportWith(targetModule.DefaultImporter);

            GetAwaiterMethod = TaskType
                .ToTypeDefOrRef()
                .CreateMemberReference("GetAwaiter", MethodSignature.CreateInstance(factory.CorLibScope
                    .CreateTypeReference("System.Runtime.CompilerServices", "TaskAwaiter`1")
                    .ToTypeSignature(true)
                    .MakeGenericInstanceType(new GenericParameterSignature(GenericParameterType.Type, 0))))
                .ImportWith(targetModule.DefaultImporter);

            GetResultMethod = TaskAwaiterType
                .ToTypeDefOrRef()
                .CreateMemberReference("GetResult", MethodSignature.CreateInstance(
                    new GenericParameterSignature(GenericParameterType.Type, 0)))
                .ImportWith(targetModule.DefaultImporter);
        }
    }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncVoidMethodBuilder"/> or <see cref="AsyncTaskMethodBuilder{TResult}"/> type.
    /// </summary>
    public ITypeDefOrRef Type { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.Create"/> method.
    /// </summary>
    public MemberReference CreateMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.Start{TStateMachine}"/> method.
    /// </summary>
    public MemberReference StartMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.SetStateMachine"/> method.
    /// </summary>
    public MemberReference SetStateMachineMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.SetException"/> method.
    /// </summary>
    public MemberReference SetExceptionMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.SetResult"/> method.
    /// </summary>
    public MemberReference SetResult { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.AwaitOnCompleted{TAwaiter,TStateMachine}"/> method.
    /// </summary>
    public MemberReference AwaitOnCompleted { get; }

    /// <summary>
    /// Gets a reference to the <see cref="AsyncTaskMethodBuilder{TResult}.get_Task" /> method.
    /// </summary>
    public MemberReference GetTaskMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="Task{TResult}"/> type associated to the factory.
    /// </summary>
    public TypeSignature TaskType { get; }

    /// <summary>
    /// Gets a reference to the <see cref="Task{TResult}.GetAwaiter"/> method.
    /// </summary>
    public MemberReference GetAwaiterMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="TaskAwaiter{TResult}"/> structure.
    /// </summary>
    public TypeSignature TaskAwaiterType { get; }

    /// <summary>
    /// Gets a reference to the <see cref="TaskAwaiter{TResult}.GetResult"/> method.
    /// </summary>
    public MemberReference GetResultMethod { get; }
}