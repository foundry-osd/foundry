// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Foundry.PostInstall.Execution;
using Microsoft.Win32.SafeHandles;

namespace Foundry.PostInstall.Windows;

internal sealed class SupervisedProcess : IDisposable
{
    private readonly SafeFileHandle job;
    public Process Process { get; }
    public StreamReader StandardOutput { get; }
    public StreamReader StandardError { get; }

    private SupervisedProcess(SafeFileHandle job, Process process, StreamReader output, StreamReader error)
    { this.job = job; Process = process; StandardOutput = output; StandardError = error; }

    public static SupervisedProcess Start(ProcessCommand command)
    {
        SafeFileHandle job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var information = new ProcessInformation();
        IntPtr attributes = IntPtr.Zero;
        IntPtr handles = IntPtr.Zero;
        bool attributesInitialized = false;
        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributesInitialized = true;
            handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handles, 0, input.ClientSafePipeHandle.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, output.ClientSafePipeHandle.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size * 2, error.ClientSafePipeHandle.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, (nuint)0x20002, handles, (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = 0x100,
                    Input = input.ClientSafePipeHandle.DangerousGetHandle(),
                    Output = output.ClientSafePipeHandle.DangerousGetHandle(),
                    Error = error.ClientSafePipeHandle.DangerousGetHandle()
                },
                Attributes = attributes
            };
            string arguments = string.Join(" ", command.Arguments.Select(argument =>
                argument.Length == 0 || argument.Any(character => char.IsWhiteSpace(character) || character == '"')
                    ? WindowsArguments.Quote(argument) : argument));
            if (command.RawArguments is not null) arguments += " " + command.RawArguments;
            var line = new StringBuilder(WindowsArguments.Quote(command.FileName) + " " + arguments);
            // Assignment happens before the primary thread runs, so fast children cannot escape the job.
            if (!CreateProcess(command.FileName, line, IntPtr.Zero, IntPtr.Zero, true, 0x08080004,
                    IntPtr.Zero, command.WorkingDirectory, ref startup, out information))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!AssignProcessToJobObject(job, information.Process)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Process process = Process.GetProcessById(information.ProcessId);
            if (ResumeThread(information.Thread) == uint.MaxValue)
            { process.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();
            return new(job, process, new StreamReader(output, Encoding.UTF8, true), new StreamReader(error, Encoding.UTF8, true));
        }
        catch
        {
            if (information.Process != IntPtr.Zero) TerminateProcess(information.Process, 3);
            job.Dispose(); output.Dispose(); error.Dispose();
            throw;
        }
        finally
        {
            if (information.Thread != IntPtr.Zero) CloseHandle(information.Thread);
            if (information.Process != IntPtr.Zero) CloseHandle(information.Process);
            if (attributes != IntPtr.Zero) { if (attributesInitialized) DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
        }
    }

    public async Task WaitForChildrenAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!QueryInformationJobObject(job, 1, out Accounting accounting, Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (accounting.ActiveProcesses == 0) return;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Terminate() => TerminateJobObject(job, 3);
    public void Dispose() { job.Dispose(); Process.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size; public IntPtr Reserved; public IntPtr Desktop; public IntPtr Title;
        public uint X; public uint Y; public uint XSize; public uint YSize; public uint XCountChars; public uint YCountChars;
        public uint FillAttribute; public uint Flags; public ushort ShowWindow; public ushort ReservedSize;
        public IntPtr ReservedBytes; public IntPtr Input; public IntPtr Output; public IntPtr Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process; public IntPtr Thread; public int ProcessId; public int ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessUserTime; public long JobUserTime; public uint LimitFlags;
        public nuint MinimumWorkingSet; public nuint MaximumWorkingSet; public uint ActiveProcessLimit;
        public nuint Affinity; public uint PriorityClass; public uint SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations; public ulong WriteOperations; public ulong OtherOperations; public ulong ReadBytes; public ulong WriteBytes; public ulong OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory; public nuint JobMemory; public nuint PeakProcessMemory; public nuint PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting { public long UserTime; public long KernelTime; public long PeriodUserTime; public long PeriodKernelTime; public uint PageFaultCount; public uint TotalProcesses; public uint ActiveProcesses; public uint TerminatedProcesses; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateJobObjectW")]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits information, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting information, int length, IntPtr returnedLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
}
