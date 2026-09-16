using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Runs shell commands (cmd.exe on Windows, /bin/sh elsewhere) without blocking
/// the Unity main thread. Output lines and the exit code are delivered on the
/// main thread through callbacks.
/// </summary>
public class ShellRunner : MonoBehaviour
{
    public class Job
    {
        public string Command;
        public string WorkingDirectory;
        public Action<string, bool> OnLine;   // (line, isError)
        public Action<int> OnExit;            // exit code, -1 when the process could not start
        public bool IsRunning { get; internal set; }
        internal Process Process;
        internal readonly ConcurrentQueue<Tuple<string, bool>> Lines = new ConcurrentQueue<Tuple<string, bool>>();
        internal int? ExitCode;
        internal bool Delivered;
    }

    private static ShellRunner instance;
    private readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
    private readonly System.Collections.Generic.List<Job> active = new System.Collections.Generic.List<Job>();

    public static ShellRunner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("ShellRunner");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ShellRunner>();
            }
            return instance;
        }
    }

    public static bool IsWindows =>
        Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

    /// <summary>Starts a command and returns immediately. Callbacks fire on the main thread.</summary>
    public Job Run(string command, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
    {
        var job = new Job
        {
            Command = command,
            WorkingDirectory = workingDirectory,
            OnLine = onLine,
            OnExit = onExit,
            IsRunning = true
        };

        var psi = new ProcessStartInfo
        {
            FileName = IsWindows ? "cmd.exe" : "/bin/sh",
            Arguments = IsWindows ? "/c " + command : "-c \"" + command.Replace("\"", "\\\"") + "\"",
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => { if (e.Data != null) job.Lines.Enqueue(Tuple.Create(e.Data, false)); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) job.Lines.Enqueue(Tuple.Create(e.Data, true)); };
            process.Exited += (s, e) =>
            {
                try
                {
                    process.WaitForExit(); // no timeout => waits until the async stdout/stderr streams hit EOF
                    job.ExitCode = process.ExitCode;
                }
                catch { job.ExitCode = -1; }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            job.Process = process;
        }
        catch (Exception ex)
        {
            job.Lines.Enqueue(Tuple.Create("Could not start process: " + ex.Message, true));
            job.ExitCode = -1;
        }

        jobs.Enqueue(job);
        return job;
    }

    public void Kill(Job job)
    {
        if (job?.Process == null) return;
        try { if (!job.Process.HasExited) job.Process.Kill(); }
        catch (Exception ex) { UnityEngine.Debug.LogWarning("[ShellRunner] Kill failed: " + ex.Message); }
    }

    private void Update()
    {
        while (jobs.TryDequeue(out Job newJob)) active.Add(newJob);

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Job job = active[i];
            while (job.Lines.TryDequeue(out Tuple<string, bool> line))
                job.OnLine?.Invoke(line.Item1, line.Item2);

            // Deliver exit only after the output queue has drained.
            if (job.ExitCode.HasValue && job.Lines.IsEmpty && !job.Delivered)
            {
                job.Delivered = true;
                job.IsRunning = false;
                job.Process?.Dispose();
                job.OnExit?.Invoke(job.ExitCode.Value);
                active.RemoveAt(i);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (Job job in active) Kill(job);
    }
}
