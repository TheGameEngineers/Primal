// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PrimalEditor.GameDev;

enum BuildConfiguration
{
    Debug,
    DebugEditor,
    Release,
    ReleaseEditor,
}

static class VisualStudio
{
    private static readonly string _progID = "VisualStudio.DTE.18.0";

    // Thread-safe lock-free initialization of the STA Thread
    private static readonly Lazy<Task> _staThreadInitializer = new(InitializeStaThread);
    private static Dispatcher _staDispatcher;

    // High-level operation lock (builds, open/close, add-files, run/stop cannot interleave)
    private static readonly SemaphoreSlim _operationLock = new(1, 1);

    // Explicit delegate instances to guarantee clean COM event subscription/unsubscription
    private static readonly EnvDTE._dispBuildEvents_OnBuildProjConfigBeginEventHandler _onBuildBeginDelegate = OnBuildSolutionBegin;
    private static readonly EnvDTE._dispBuildEvents_OnBuildProjConfigDoneEventHandler _onBuildDoneDelegate = OnBuildSolutionDone;

    // State owned by the STA thread
    private static EnvDTE80.DTE2 _vsInstance;
    private static EnvDTE.BuildEvents _buildEvents;

    // Modernized TaskCompletionSource for strictly passing the build boolean success/failure
    private static TaskCompletionSource<bool> _buildTcs;
    private static volatile bool _buildSucceeded = true;

    public static bool BuildSucceeded => _buildSucceeded;

    public static string GetConfigurationName(BuildConfiguration config) => config.ToString();

    private static Task InitializeStaThread()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                MessageFilter.Register();
                _staDispatcher = Dispatcher.CurrentDispatcher;
                tcs.TrySetResult(); // Signal that the dispatcher is ready

                // Dispatcher.Run() processes queued actions AND pumps incoming COM messages/events
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex); // Unblocks callers with an exception
            }
            finally
            {
                // Guarantee the COM Message Filter is revoked when the dispatcher shuts down
                MessageFilter.Revoke();
            }
        })
        {
            IsBackground = true,
            Name = "VS-DTE-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private static async Task RunOnStaAsync(Action action)
    {
        await _staThreadInitializer.Value.ConfigureAwait(false);
        await _staDispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
    }

    private static async Task<T> RunOnStaAsync<T>(Func<T> func)
    {
        await _staThreadInitializer.Value.ConfigureAwait(false);
        return await _staDispatcher.InvokeAsync(func).Task.ConfigureAwait(false);
    }

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable pprot);

    private static void ReleaseComObject<T>(ref T comObject) where T : class
    {
        // Extract and null out the reference in a thread-safe manner
        var target = Interlocked.Exchange(ref comObject, null);
        if (target != null && Marshal.IsComObject(target))
        {
            void Release()
            {
                try { Marshal.ReleaseComObject(target); }
                catch { /* Detaching safely */ }
            }

            if (Dispatcher.CurrentDispatcher == _staDispatcher)
                Release();
            else
                _staDispatcher?.InvokeAsync(Release);
        }
    }

    private static bool IsVsInstanceAlive()
    {
        if (_vsInstance == null) return false;

        try
        {
            // Lightweight call to verify connection is alive (handles user closing VS externally)
            _ = _vsInstance.Name;
            return true;
        }
        catch
        {
            ReleaseComObject(ref _vsInstance);
            _buildEvents = null;
            return false;
        }
    }

    public static async Task OpenVisualStudioAsync(string solutionPath)
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await RunOnStaAsync(() => OpenVisualStudio_Internal(solutionPath)).ConfigureAwait(false);
        }
        finally { _operationLock.Release(); }
    }

    private static void OpenVisualStudio_Internal(string solutionPath)
    {
        if (IsVsInstanceAlive() &&
            string.Equals(_vsInstance.Solution?.FullName, solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IRunningObjectTable rot = null;
        IEnumMoniker monikersTable = null;
        IBindCtx bindCtx = null;
        try
        {
            var hResult = GetRunningObjectTable(0, out rot);
            Marshal.ThrowExceptionForHR(hResult); // Natively maps COM HRESULTs to .NET Exceptions
            if (rot == null) throw new COMException("GetRunningObjectTable() returned null.");

            rot.EnumRunning(out monikersTable);
            monikersTable.Reset();

            hResult = CreateBindCtx(0, out bindCtx);
            Marshal.ThrowExceptionForHR(hResult);
            if (bindCtx == null) throw new COMException("CreateBindCtx() returned null.");

            IMoniker[] currentMoniker = new IMoniker[1];
            while (monikersTable.Next(1, currentMoniker, IntPtr.Zero) == 0)
            {
                var moniker = currentMoniker[0];
                object obj = null;
                try
                {
                    if (moniker == null) continue;

                    moniker.GetDisplayName(bindCtx, null, out string name);
                    if (name?.Contains(_progID) != true) continue;

                    hResult = rot.GetObject(moniker, out obj);
                    if (hResult < 0 || obj == null) continue;

                    if (obj is EnvDTE80.DTE2 dte)
                    {
                        string solutionName = null;
                        try { solutionName = dte.Solution?.FullName; } catch { }

                        if (string.Equals(solutionName, solutionPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _vsInstance = dte;
                            obj = null; // Prevent releasing valid reference in finally block
                            break;
                        }
                    }
                }
                finally
                {
                    if (obj != null) ReleaseComObject(ref obj);
                    ReleaseComObject(ref moniker);
                    currentMoniker[0] = null;
                }
            }

            if (_vsInstance == null)
            {
                Type visualStudioType = Type.GetTypeFromProgID(_progID, true);
                _vsInstance = Activator.CreateInstance(visualStudioType) as EnvDTE80.DTE2;
            }
        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Failed to open Visual Studio: {ex}");
            throw;
        }
        finally
        {
            ReleaseComObject(ref monikersTable);
            ReleaseComObject(ref rot);
            ReleaseComObject(ref bindCtx);
        }
    }

    private static void CloseVisualStudio_Internal()
    {
        if (!IsVsInstanceAlive()) return;

        try
        {
            UnsubscribeBuildEvents();

            if (_vsInstance.Solution?.IsOpen == true)
            {
                _vsInstance.ExecuteCommand("File.SaveAll");
                _vsInstance.Solution.Close(true);
            }

            _vsInstance.Quit();

        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Failed to close Visual Studio: {ex}");
        }
        finally
        {
            ReleaseComObject(ref _vsInstance);
            _buildEvents = null;

            // Force GC to clean up implicit RCWs and allow devenv.exe to terminate
            // NOTE: this can be removed if it cause noticeable UI freezes.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    public static async Task CloseVisualStudioAsync()
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await RunOnStaAsync(CloseVisualStudio_Internal).ConfigureAwait(false);
        }
        finally { _operationLock.Release(); }
    }

    public static async Task<bool> AddFilesToSolutionAsync(string solution, string projectName, string[] files)
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RunOnStaAsync(() => AddFilesToSolution_Internal(solution, projectName, files));
        }
        finally { _operationLock.Release(); }
    }

    private static bool AddFilesToSolution_Internal(string solution, string projectName, string[] files)
    {
        if (files == null || files.Length == 0) return false;

        try
        {
            OpenVisualStudio_Internal(solution);
            if (!IsVsInstanceAlive())
                throw new InvalidOperationException("Could not connect to Visual Studio.");

            if (!_vsInstance.Solution.IsOpen)
                _vsInstance.Solution.Open(solution);
            else
                _vsInstance.ExecuteCommand("File.SaveAll");

            var project = _vsInstance.Solution.Projects
                .Cast<EnvDTE.Project>()
                .FirstOrDefault(x => string.Equals(x.Name, projectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Could not find project {projectName} in the solution.");

            dynamic vcProject = project.Object;
            foreach (var file in files)
                vcProject.AddFile(file);

            _vsInstance.ExecuteCommand("File.SaveAll");

            var cpp = files.FirstOrDefault(x => Path.GetExtension(x).Equals(".cpp", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(cpp))
            {
                _vsInstance.ItemOperations.OpenFile(cpp, EnvDTE.Constants.vsViewKindTextView).Visible = true;
            }

            _vsInstance.MainWindow.Activate();
            _vsInstance.MainWindow.Visible = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Failed to add files to Visual Studio project: {ex}");
            return false;
        }
    }

    private static void UnsubscribeBuildEvents()
    {
        var events = _buildEvents;
        _buildEvents = null;
        if (events == null) return;

        try
        {
            events.OnBuildProjConfigBegin -= _onBuildBeginDelegate;
            events.OnBuildProjConfigDone -= _onBuildDoneDelegate;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            ReleaseComObject(ref events);
        }
    }

    private static void OnBuildSolutionBegin(string project, string projectConfig, string platform, string solutionConfig)
    {
        Logger.Log(MessageType.Info, $"Building {project}, {projectConfig}, {platform}, {solutionConfig}");
    }

    private static void OnBuildSolutionDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
    {
        Logger.Log(success ? MessageType.Info : MessageType.Error,
            $"Building {projectConfig} configuration {(success ? "succeeded" : "failed")}");

        _buildSucceeded = success;

        // Atomically complete the TCS passing the boolean success state
        var tcs = Interlocked.Exchange(ref _buildTcs, null);
        tcs?.TrySetResult(success);
    }

    public static async Task<bool> BuildSolutionAsync(
        Project project,
        BuildConfiguration buildConfig,
        bool showWindow = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildSolution_Internal(project, buildConfig, showWindow, timeout ?? TimeSpan.FromMinutes(30), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { _operationLock.Release(); }
    }

    private static async Task<bool> BuildSolution_Internal(
        Project project,
        BuildConfiguration buildConfig,
        bool showWindow,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (await IsDebuggingAsync().ConfigureAwait(false))
        {
            Logger.Log(MessageType.Error, "Visual Studio is currently running a process.");
            return false;
        }

        // PDB Cleanup BEFORE triggering build to eliminate file lock conflicts
        await Task.Run(() =>
        {
            try
            {
                var configName = GetConfigurationName(buildConfig);
                foreach (var pdbFile in Directory.GetFiles(Path.Combine($"{project.Path}", $@"x64\{configName}"), "*.pdb"))
                    File.Delete(pdbFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.Log(MessageType.Warning, $"Could not clean old PDBs: {ex.Message}");
            }
        }, cancellationToken).ConfigureAwait(false);

        await RunOnStaAsync(() => OpenVisualStudio_Internal(project.Solution)).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _buildTcs, tcs);
        _buildSucceeded = false;

        try
        {
            await RunOnStaAsync(() =>
            {
                if (!IsVsInstanceAlive()) return;

                UnsubscribeBuildEvents();

                _buildEvents = _vsInstance.Events.BuildEvents;
                _buildEvents.OnBuildProjConfigBegin += _onBuildBeginDelegate;
                _buildEvents.OnBuildProjConfigDone += _onBuildDoneDelegate;

                _vsInstance.MainWindow.Visible = showWindow;
                _vsInstance.SuppressUI = !showWindow; // Prevent blocking modal dialogs

                if (!_vsInstance.Solution.IsOpen)
                    _vsInstance.Solution.Open(project.Solution);

                var configName = GetConfigurationName(buildConfig);
                var configs = _vsInstance.Solution.SolutionBuild.SolutionConfigurations;
                try { configs.Item(configName).Activate(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Solution configuration '{configName}' not found.", ex);
                }
                _vsInstance.ExecuteCommand("Build.BuildSolution");
            }).ConfigureAwait(false);

            // Natively handles Timeout, Cancellation, and directly extracts the boolean result
            return await tcs.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            Logger.Log(MessageType.Error, ex is TimeoutException
                ? "Build timed out."
                : "Build was cancelled.");

            await TryCancelBuildAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _buildTcs, null);
            await RunOnStaAsync(UnsubscribeBuildEvents).ConfigureAwait(false);
        }
    }

    private static async Task TryCancelBuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunOnStaAsync(() =>
            {
                if (IsVsInstanceAlive())
                    _vsInstance.ExecuteCommand("Build.Cancel");
            }).ConfigureAwait(false);
        }
        catch { }
    }

    public static async Task<bool> IsDebuggingAsync()
    {
        return await RunOnStaAsync(IsDebugging_Internal);
    }

    private static bool IsDebugging_Internal()
    {
        if (!IsVsInstanceAlive()) return false;
        try
        {
            var debugger = _vsInstance.Debugger;
            return debugger?.CurrentProgram != null ||
                   debugger?.CurrentMode == EnvDTE.dbgDebugMode.dbgRunMode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task RunAsync(bool debug)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            Logger.Log(MessageType.Warning, "Timed out waiting to run the project.");
            return;
        }

        try
        {
            await RunOnStaAsync(() =>
            {
                if (!IsVsInstanceAlive() || !_buildSucceeded) return;
                if (IsDebugging_Internal()) return;

                try
                {
                    _vsInstance.ExecuteCommand(debug ? "Debug.Start" : "Debug.StartWithoutDebugging");
                }
                catch (Exception ex)
                {
                    Logger.Log(MessageType.Error, $"Failed to start execution: {ex.Message}");
                }
            });
        }
        finally { _operationLock.Release(); }
    }

    public static async Task StopAsync()
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            Logger.Log(MessageType.Warning, "Timed out waiting to stop the project.");
            return;
        }

        try
        {
            await RunOnStaAsync(() =>
            {
                if (!IsVsInstanceAlive()) return;
                if (!IsDebugging_Internal()) return;

                try
                {
                    _vsInstance.ExecuteCommand("Debug.StopDebugging");
                }
                catch (Exception ex)
                {
                    Logger.Log(MessageType.Error, $"Failed to stop debugging: {ex.Message}");
                }
            });
        }
        finally { _operationLock.Release(); }
    }
}

[GeneratedComClass]
partial class MessageFilter : IMessageFilter
{
    private const int SERVERCALL_ISHANDLED = 0;
    private const int PENDINGMSG_WAITDEFPROCESS = 2;
    private const int SERVERCALL_RETRYLATER = 2;

    [DllImport("Ole32.dll")]
    private static extern int CoRegisterMessageFilter(IMessageFilter newFilter, out IMessageFilter oldFilter);

    public static void Register()
    {
        IMessageFilter newFilter = new MessageFilter();
        int hr = CoRegisterMessageFilter(newFilter, out _);
        Debug.Assert(hr >= 0, "Registering COM IMessageFilter failed.");
    }

    public static void Revoke()
    {
        int hr = CoRegisterMessageFilter(null, out _);
        Debug.Assert(hr >= 0, "Unregistering COM IMessageFilter failed.");
    }

    int IMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => SERVERCALL_ISHANDLED;

    int IMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        if (dwRejectType == SERVERCALL_RETRYLATER)
        {
            Debug.WriteLine("COM server busy. Retrying call to EnvDTE interface.");
            return 500;
        }
        return -1;
    }

    int IMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => PENDINGMSG_WAITDEFPROCESS;
}

[GeneratedComInterface, Guid("00000016-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
partial interface IMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}