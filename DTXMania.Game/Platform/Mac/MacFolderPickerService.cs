#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Platform
{
    /// <summary>macOS folder picker implemented with AppleScript Standard Additions.</summary>
    internal sealed class MacFolderPickerService : IFolderPickerService
    {
        private const string OsaScriptPath = "/usr/bin/osascript";
        private const string CancelledMarker = "__DTXMANIA_FOLDER_PICKER_CANCELLED__";

        public async Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return FolderPickerResult.Cancelled();

            Process? process = null;
            try
            {
                process = Process.Start(CreateStartInfo(initialDirectory));
                if (process == null)
                    return FolderPickerResult.Unavailable("Unable to start the macOS folder picker.");

                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    StopProcess(process);
                    await ObserveQuietlyAsync(outputTask, errorTask).ConfigureAwait(false);
                    return FolderPickerResult.Cancelled();
                }

                var standardOutput = await outputTask.ConfigureAwait(false);
                var standardError = await errorTask.ConfigureAwait(false);
                return MapProcessResult(process.ExitCode, standardOutput, standardError);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StopProcess(process);
                return FolderPickerResult.Cancelled();
            }
            catch (Win32Exception exception)
            {
                return FolderPickerResult.Unavailable(exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return FolderPickerResult.Failed(exception.Message);
            }
            catch (Exception exception)
            {
                return FolderPickerResult.Failed(exception.Message);
            }
            finally
            {
                process?.Dispose();
            }
        }

        internal static ProcessStartInfo CreateStartInfo(string? initialDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OsaScriptPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(BuildAppleScript(initialDirectory));
            return startInfo;
        }

        internal static FolderPickerResult MapProcessResult(
            int exitCode,
            string? standardOutput,
            string? standardError)
        {
            var output = standardOutput?.Trim() ?? string.Empty;
            var error = standardError?.Trim() ?? string.Empty;
            var details = string.IsNullOrWhiteSpace(error) ? output : error;

            if (exitCode == 0)
            {
                if (string.Equals(output, CancelledMarker, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(output))
                {
                    return FolderPickerResult.Cancelled();
                }

                return FolderPickerResult.Selected(output);
            }

            if (details.Contains("-128", StringComparison.Ordinal) ||
                details.Contains("User canceled", StringComparison.OrdinalIgnoreCase))
            {
                return FolderPickerResult.Cancelled();
            }

            // -1743 is the Apple event authorization-denied code. It is a real
            // failure, not a user cancellation, so the overlay keeps its draft open.
            if (details.Contains("-1743", StringComparison.Ordinal) ||
                details.Contains("not authorized", StringComparison.OrdinalIgnoreCase))
            {
                return FolderPickerResult.Failed(
                    string.IsNullOrWhiteSpace(details)
                        ? "macOS denied folder-picker authorization."
                        : details);
            }

            return FolderPickerResult.Failed(
                string.IsNullOrWhiteSpace(details)
                    ? $"macOS folder picker exited with code {exitCode}."
                    : details);
        }

        private static string BuildAppleScript(string? initialDirectory)
        {
            var defaultLocation = !string.IsNullOrWhiteSpace(initialDirectory) &&
                                  Directory.Exists(initialDirectory)
                ? $" default location POSIX file \"{EscapeAppleScriptString(initialDirectory)}\""
                : string.Empty;

            return "try\n" +
                   "set selectedFolder to choose folder with prompt \"Choose song folder\"" + defaultLocation + "\n" +
                   "return POSIX path of selectedFolder\n" +
                   "on error number -128\n" +
                   $"return \"{CancelledMarker}\"\n" +
                   "end try";
        }

        private static string EscapeAppleScriptString(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        private static void StopProcess(Process? process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }
            catch (Win32Exception)
            {
                // Best effort only; the cancellation result is still authoritative.
            }
            catch (AggregateException)
            {
                // process-tree termination may report partial failures (e.g. a
                // child already exited). Best effort only; cancellation remains
                // authoritative.
            }
        }

        /// <summary>
        /// Awaits the redirected-stream read tasks so they finish or are observed
        /// before the process is disposed, suppressing cancellation or failure
        /// that is expected once the process has been killed.
        /// </summary>
        private static async Task ObserveQuietlyAsync(Task outputTask, Task errorTask)
        {
            async Task ObserveOneAsync(Task task)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the cancellation token was passed to ReadToEndAsync.
                }
                catch (Exception)
                {
                    // The redirected stream read failed after process kill;
                    // the cancellation result is still authoritative.
                }
            }

            await Task.WhenAll(ObserveOneAsync(outputTask), ObserveOneAsync(errorTask))
                .ConfigureAwait(false);
        }
    }
}
