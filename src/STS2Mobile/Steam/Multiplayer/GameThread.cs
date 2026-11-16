using System.Runtime.CompilerServices;
using Godot;

namespace STS2Mobile.Steam.Multiplayer;

internal static class GameThread
{
    private static int thread;
    public static void Initialize() => thread = System.Environment.CurrentManagedThreadId;
    public static Awaiter Switch() => new();
    internal readonly struct Awaiter : INotifyCompletion
    {
        public Awaiter GetAwaiter() => this;
        public bool IsCompleted => thread != 0 && System.Environment.CurrentManagedThreadId == thread;
        public void OnCompleted(Action continuation) => Callable.From(continuation).CallDeferred();
        public void GetResult() { }
    }
}
