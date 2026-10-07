// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment.Native.Imaging;
using Foundry.Deploy.Services.Deployment.Native.Servicing;

namespace Foundry.Deploy.Services.Deployment.Native;

/// <summary>Runs before WPF, telemetry or metadata readers acquire native libraries in the child process.</summary>
internal static class NativeDeploymentWorker
{
    public const string Command = "--native-deployment-worker";

    public static bool IsWorkerInvocation(string[] args) => args.Length > 0 && args[0] == Command;

    public static int Run(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        object outputLock = new();
        void Emit(NativeWorkerMessage message)
        {
            lock (outputLock)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(message));
                Console.Out.Flush();
            }
        }
        try
        {
            if (args.Length != 2) throw new ArgumentException("A native worker requires one request file.");
            var info = new FileInfo(args[1]);
            if (info.Length > 64 * 1024) throw new InvalidDataException("The native worker request is too large.");
            NativeWorkerRequest request = JsonSerializer.Deserialize<NativeWorkerRequest>(File.ReadAllText(info.FullName))
                ?? throw new InvalidDataException("The native worker request is empty.");
            if (!Enum.IsDefined(request.Operation)) throw new InvalidDataException("The native worker operation is unsupported.");
            NativeWorkerResult result = request.Operation is NativeWorkerOperation.ProbeWim or NativeWorkerOperation.ApplyWim
                ? new NativeWimOperations().Execute(request, Emit)
                : new NativeDismOperations().Execute(request, Emit);
            Emit(new() { Kind = "complete", Result = result });
            return 0;
        }
        catch (Exception exception)
        {
            NativeWorkerError error = exception is NativeOperationException native
                ? new(native.Function, native.ErrorCode, native.Message, [.. native.CleanupErrors])
                : new("native_worker", exception.HResult, exception.Message, []);
            Emit(new() { Kind = "complete", Error = error });
            return 1;
        }
    }
}
