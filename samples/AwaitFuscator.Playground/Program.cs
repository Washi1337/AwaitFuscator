using System.Runtime.CompilerServices;

namespace AwaitFuscator.Playground;

public static class Program
{
    public static async Task Main()
    {
        await await await 1337;
    }

    public static IntAwaiter GetAwaiter(this int self) => new();
    public static DoubleAwaiter GetAwaiter(this double self) => new();
    public static FloatAwaiter GetAwaiter(this float self) => new();

    public readonly struct IntAwaiter : INotifyCompletion
    {
        public bool IsCompleted => true;
        public void OnCompleted(Action continuation) {}
        public double GetResult() { Console.WriteLine("Who needs actual statements"); return 1337.0; }
    }

    public readonly struct DoubleAwaiter : INotifyCompletion
    {
        public bool IsCompleted => true;
        public void OnCompleted(Action continuation) {}
        public float GetResult() { Console.WriteLine("when all you need"); return 1337f; }
    }

    public readonly struct FloatAwaiter : INotifyCompletion
    {
        public bool IsCompleted => true;
        public void OnCompleted(Action continuation) {}
        public void GetResult() => Console.WriteLine("is a little bit of patience!");
    }
}