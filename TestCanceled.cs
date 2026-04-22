using System;
using System.Threading;
using System.Threading.Tasks;

class Program {
    static void Main() {
        var canceledTask = Task.FromCanceled(new CancellationToken(canceled: true));
        Console.WriteLine($"IsCompleted: {canceledTask.IsCompleted}");
        Console.WriteLine($"IsCompletedSuccessfully: {canceledTask.IsCompletedSuccessfully}");
        Console.WriteLine($"IsFaulted: {canceledTask.IsFaulted}");
        Console.WriteLine($"IsCanceled: {canceledTask.IsCanceled}");
    }
}
