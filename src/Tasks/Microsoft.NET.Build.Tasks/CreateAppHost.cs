﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.Build.Framework;
using Microsoft.NET.HostModel;
using Microsoft.NET.HostModel.AppHost;

namespace Microsoft.NET.Build.Tasks
{
    /// <summary>
    /// Creates the runtime host to be used for an application.
    /// This embeds the application DLL path into the apphost and performs additional customizations as requested.
    /// </summary>
    public class CreateAppHost : TaskBase
    {
        /// <summary>
        /// The number of additional retries to attempt for creating the apphost.
        /// <summary>
        /// <remarks>
        /// The default is no retries because internally the `HostWriter` attempts to retry
        /// on different I/O operations. Users can optionally retry at the task level if desired.
        /// </remarks>
        public const int DefaultRetries = 0;

        /// The default delay, in milliseconds, for each retry attempt for creating the apphost.
        /// </summary>
        public const int DefaultRetryDelayMilliseconds = 1000;

        [Required]
        public string AppHostSourcePath { get; set; }

        [Required]
        public string AppHostDestinationPath { get; set; }

        [Required]
        public string AppBinaryName { get; set; }

        [Required]
        public string IntermediateAssembly { get; set; }

        public bool WindowsGraphicalUserInterface { get; set; }

        public int Retries { get; set; } = DefaultRetries;

        public int RetryDelayMilliseconds { get; set; } = DefaultRetryDelayMilliseconds;

        public bool EnableMacOSCodeSign { get; set; } = false;

        public bool DisableCetCompat { get; set; } = false;

        public ITaskItem[] DotNetSearchLocations { get; set; }

        public string AppRelativeDotNet { get; set; } = null;

        protected override void ExecuteCore()
        {
            try
            {
                var isGUI = WindowsGraphicalUserInterface;
                var resourcesAssembly = IntermediateAssembly;

                int attempts = 0;

                while (true)
                {
                    try
                    {
                        HostWriter.DotNetSearchOptions options = null;
                        if (DotNetSearchLocations?.Length > 0)
                        {
                            HostWriter.DotNetSearchOptions.SearchLocation searchLocation = default;
                            foreach (var locationItem in DotNetSearchLocations)
                            {
                                if (Enum.TryParse(locationItem.ItemSpec, out HostWriter.DotNetSearchOptions.SearchLocation location)
                                    && Enum.IsDefined(typeof(HostWriter.DotNetSearchOptions.SearchLocation), location))
                                {
                                    searchLocation |= location;
                                }
                                else
                                {
                                    throw new BuildErrorException(Strings.InvalidAppHostDotNetSearch, locationItem.ItemSpec);
                                }
                            }

                            options = new HostWriter.DotNetSearchOptions()
                            {
                                Location = searchLocation,
                                AppRelativeDotNet = AppRelativeDotNet
                            };
                        }

                        HostWriter.CreateAppHost(appHostSourceFilePath: AppHostSourcePath,
                                                appHostDestinationFilePath: AppHostDestinationPath,
                                                appBinaryFilePath: AppBinaryName,
                                                windowsGraphicalUserInterface: isGUI,
                                                assemblyToCopyResourcesFrom: resourcesAssembly,
                                                enableMacOSCodeSign: EnableMacOSCodeSign,
                                                disableCetCompat: DisableCetCompat,
                                                dotNetSearchOptions: options);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException ||
                                               ex is UnauthorizedAccessException ||
                                               (ex is AggregateException && (ex.InnerException is IOException || ex.InnerException is UnauthorizedAccessException)))
                    {
                        if (Retries < 0 || attempts == Retries)
                        {
                            throw;
                        }

                        ++attempts;

                        string message = ex.Message;
                        if (ex is AggregateException)
                        {
                            message = ex.InnerException.Message;
                        }

                        Log.LogWarning(
                            string.Format(Strings.AppHostCreationFailedWithRetry,
                                attempts,
                                Retries + 1,
                                message));

                        if (RetryDelayMilliseconds > 0)
                        {
                            Thread.Sleep(RetryDelayMilliseconds);
                        }
                    }
                }
            }
            catch (AppNameTooLongException ex)
            {
                throw new BuildErrorException(Strings.FileNameIsTooLong, ex.LongName);
            }
            catch (AppHostSigningException ex)
            {
                throw new BuildErrorException(Strings.AppHostSigningFailed, ex.Message, ex.ExitCode.ToString());
            }
            catch (PlaceHolderNotFoundInAppHostException ex)
            {
                throw new BuildErrorException(Strings.AppHostHasBeenModified, AppHostSourcePath, BitConverter.ToString(ex.MissingPattern));
            }
        }
    }
}
