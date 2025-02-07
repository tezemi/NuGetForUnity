﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NugetForUnity.Helper;
using NugetForUnity.Models;
using NugetForUnity.PackageSource;
using NugetForUnity.PluginSupport;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("NuGetForUnity.Editor.Tests")]

namespace NugetForUnity.Configuration
{
    /// <summary>
    ///     Manages the active configuration of NuGetForUnity <see cref="NugetConfigFile" />.
    /// </summary>
    public static class ConfigurationManager
    {
        /// <summary>
        ///     The <see cref="INugetPackageSource" /> to use.
        /// </summary>
        [CanBeNull]
        private static INugetPackageSource activePackageSource;

        /// <summary>
        ///     Backing field for the NuGet.config file.
        /// </summary>
        [CanBeNull]
        private static NugetConfigFile nugetConfigFile;

        static ConfigurationManager()
        {
            NugetConfigFileDirectoryPath = EditorPrefs.GetString(nameof(NugetConfigFileDirectoryPath), string.Empty);
            NugetConfigFilePath = Path.Combine(NugetConfigFileDirectoryPath, NugetConfigFile.FileName);
        }

        /// <summary>
        ///     Gets the path to the nuget.config file.
        /// </summary>
        /// <remarks>
        ///     <see cref="NugetConfigFile" />.
        /// </remarks>
        [NotNull]
        public static string NugetConfigFilePath { get; private set; }

        /// <summary>
        /// Gets the absolute path to the nuget.config file.
        /// </summary>
        public static string FullNugetConfigFilePath => Path.Combine(Application.dataPath, NugetConfigFilePath);

        /// <summary>
        ///     Gets the loaded NuGet.config file that holds the settings for NuGet.
        /// </summary>
        [NotNull]
        public static NugetConfigFile NugetConfigFile
        {
            get
            {
                if (nugetConfigFile is null)
                {
                    LoadNugetConfigFile();
                }

                Debug.Assert(nugetConfigFile != null, nameof(nugetConfigFile) + " != null");
                return nugetConfigFile;
            }
        }

        /// <summary>
        ///     Gets the path to the directory containing the NuGet.config file.
        /// </summary>
        [NotNull]
        internal static string NugetConfigFileDirectoryPath { get; set; }

        /// <summary>
        ///     Gets a value indicating whether verbose logging is enabled.
        ///     This can be set in the NuGet.config file.
        ///     But this will not load the NuGet.config file to prevent endless loops wen we log while we load the <c>NuGet.config</c> file.
        /// </summary>
        internal static bool IsVerboseLoggingEnabled => nugetConfigFile?.Verbose ?? false;

        /// <summary>
        ///     Gets the <see cref="INugetPackageSource" /> to use.
        /// </summary>
        [NotNull]
        private static INugetPackageSource ActivePackageSource
        {
            get
            {
                if (activePackageSource is null)
                {
                    LoadNugetConfigFile();
                }

                Debug.Assert(activePackageSource != null, nameof(activePackageSource) + " != null");
                return activePackageSource;
            }
        }

        /// <summary>
        ///     Loads the NuGet.config file.
        /// </summary>
        public static void LoadNugetConfigFile()
        {
            if (File.Exists(FullNugetConfigFilePath))
            {
                nugetConfigFile = NugetConfigFile.Load(FullNugetConfigFilePath);
            }
            else
            {
                Debug.LogFormat("No NuGet.config file found. Creating default at {0}", FullNugetConfigFilePath);

                nugetConfigFile = NugetConfigFile.CreateDefaultFile(FullNugetConfigFilePath);
            }

            // parse any command line arguments
            var packageSourcesFromCommandLine = new List<INugetPackageSource>();
            var readingSources = false;
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (readingSources)
                {
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        readingSources = false;
                    }
                    else
                    {
                        var source = NugetPackageSourceCreator.CreatePackageSource(
                            $"CMD_LINE_SRC_{packageSourcesFromCommandLine.Count}",
                            arg,
                            null,
                            null);
                        NugetLogger.LogVerbose("Adding command line package source {0} at {1}", source.Name, arg);
                        packageSourcesFromCommandLine.Add(source);
                    }
                }

                if (arg.Equals("-Source", StringComparison.OrdinalIgnoreCase))
                {
                    // if the source is being forced, don't install packages from the cache
                    NugetConfigFile.InstallFromCache = false;
                    readingSources = true;
                }
            }

            if (packageSourcesFromCommandLine.Count == 1)
            {
                activePackageSource = packageSourcesFromCommandLine[0];
            }
            else if (packageSourcesFromCommandLine.Count > 1)
            {
                activePackageSource = new NugetPackageSourceCombined(packageSourcesFromCommandLine);
            }
            else
            {
                // if there are not command line overrides, use the NuGet.config package sources
                activePackageSource = NugetConfigFile.ActivePackageSource;
            }

            PluginRegistry.InitPlugins();
        }

        /// <summary>
        /// Moves the nuget.config file to the specified relative path.
        /// </summary>
        /// <param name="newPath">The new path of the nuget.config file without
        /// the file name. Relative to the assets folder.</param>
        internal static void Move([NotNull] string newPath)
        {
            var oldPath = NugetConfigFileDirectoryPath;
            var oldFilePath = NugetConfigFilePath;
            var oldFullFilePath = FullNugetConfigFilePath;

            NugetConfigFileDirectoryPath = newPath;
            NugetConfigFilePath = Path.Combine(newPath, NugetConfigFile.FileName);
            EditorPrefs.SetString(nameof(NugetConfigFileDirectoryPath), newPath);        

            Debug.Log(FullNugetConfigFilePath);

            try
            {
                if (!File.Exists(oldFullFilePath))
                {
                    LoadNugetConfigFile();

                    AssetDatabase.Refresh();
                    return;
                }

                Directory.CreateDirectory(Path.Combine(Application.dataPath, NugetConfigFileDirectoryPath));

                File.Move(oldFullFilePath, FullNugetConfigFilePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);

                NugetConfigFileDirectoryPath = oldPath;
                NugetConfigFilePath = Path.Combine(oldPath, NugetConfigFile.FileName);
                EditorPrefs.SetString(nameof(NugetConfigFileDirectoryPath), oldPath);

                return;
            }

            // manually moving meta file to suppress Unity warning
            if (File.Exists($"{oldFullFilePath}.meta"))
            {
                File.Move($"{oldFullFilePath}.meta", $"{FullNugetConfigFilePath}.meta");
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        ///     Gets a list of NugetPackages from all active package source's.
        ///     This allows searching for partial IDs or even the empty string (the default) to list ALL packages.
        /// </summary>
        /// <param name="searchTerm">The search term to use to filter packages. Defaults to the empty string.</param>
        /// <param name="includePrerelease">True to include prerelease packages (alpha, beta, etc).</param>
        /// <param name="numberToGet">The number of packages to fetch.</param>
        /// <param name="numberToSkip">The number of packages to skip before fetching.</param>
        /// <param name="cancellationToken">Token that can be used to cancel the asynchronous task.</param>
        /// <returns>The list of available packages.</returns>
        [NotNull]
        [ItemNotNull]
        public static Task<List<INugetPackage>> SearchAsync(
            [NotNull] string searchTerm = "",
            bool includePrerelease = false,
            int numberToGet = 15,
            int numberToSkip = 0,
            CancellationToken cancellationToken = default)
        {
            return ActivePackageSource.SearchAsync(searchTerm, includePrerelease, numberToGet, numberToSkip, cancellationToken);
        }

        /// <summary>
        ///     Queries all active nuget package source's with the given list of installed packages to get any updates that are available.
        /// </summary>
        /// <param name="packagesToUpdate">The list of currently installed packages for witch updates are searched.</param>
        /// <param name="includePrerelease">True to include prerelease packages (alpha, beta, etc).</param>
        /// <param name="targetFrameworks">The specific frameworks to target?.</param>
        /// <param name="versionConstraints">The version constraints?.</param>
        /// <returns>A list of all updates available.</returns>
        [NotNull]
        [ItemNotNull]
        public static List<INugetPackage> GetUpdates(
            [NotNull] IEnumerable<INugetPackage> packagesToUpdate,
            bool includePrerelease = false,
            string targetFrameworks = "",
            string versionConstraints = "")
        {
            return ActivePackageSource.GetUpdates(packagesToUpdate, includePrerelease, targetFrameworks, versionConstraints);
        }

        /// <inheritdoc cref="INugetPackageSource.GetSpecificPackage(INugetPackageIdentifier)" />
        [CanBeNull]
        public static INugetPackage GetSpecificPackage([NotNull] INugetPackageIdentifier nugetPackageIdentifier)
        {
            return ActivePackageSource.GetSpecificPackage(nugetPackageIdentifier);
        }
    }
}
