﻿// Author: Dominic Beger (Trade/ProgTrade)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Ionic.Zip;
using Microsoft.Win32;
using nUpdate.UpdateInstaller.Client.GuiInterface;
using nUpdate.UpdateInstaller.Core;
using nUpdate.UpdateInstaller.Core.Operations;
using nUpdate.UpdateInstaller.UI.Dialogs;
using Newtonsoft.Json.Linq;

namespace nUpdate.UpdateInstaller
{
    public class Updater
    {
        private int _doneTaskAmount;
        private IProgressReporter _progressReporter;
        private int _totalTaskCount;
        private float _percentageMultiplier = 100f;

        /// <summary>
        ///     Runs the updating process.
        /// </summary>
        public void RunUpdate()
        {
            try
            {
                _progressReporter = GetGui();
            }
            catch (Exception ex)
            {
                _progressReporter.InitializingFail(ex);
                _progressReporter.Terminate();
                return;
            }

            ThreadPool.QueueUserWorkItem(arg => RunUpdateAsync());
            try
            {
                _progressReporter.Initialize();
            }
            catch (Exception ex)
            {
                _progressReporter.InitializingFail(ex);
                _progressReporter.Terminate();
            }
        }

        /// <summary>
        ///     Loads the GUI either from a given external assembly, if one is set, or otherwise from the integrated GUI.
        /// </summary>
        /// <returns>
        ///     Returns a new instance of the given object that implements the
        ///     <see cref="nUpdate.UpdateInstaller.Client.GuiInterface.IProgressReporter" />-interface.
        /// </returns>
        private IProgressReporter GetGui()
        {
            if (String.IsNullOrEmpty(Program.ExternalGuiAssemblyPath) || !File.Exists(Program.ExternalGuiAssemblyPath))
                return (IProgressReporter) Activator.CreateInstance(typeof (MainForm));

            var assembly = Assembly.LoadFrom(Program.ExternalGuiAssemblyPath);
            var validTypes =
                assembly.GetTypes().Where(item => typeof (IProgressReporter).IsAssignableFrom(item)).ToList();
                // If any class implements the interface, load it...
            if (validTypes.Count <= 0)
                return (IProgressReporter) Activator.CreateInstance(typeof (MainForm));

            try
            {
                return (IProgressReporter) Activator.CreateInstance(validTypes.First());
            }
            catch (Exception) // No parameterless constructor as the "CreateInstance"-method wants a default constructor
            {
                return (IProgressReporter) Activator.CreateInstance(typeof (MainForm));
            }
        }

        /// <summary>
        ///     Runs the updating process. This method does not block the calling thread.
        /// </summary>
        private void RunUpdateAsync()
        {
            try
            {
                using (var zf = ZipFile.Read(Program.PackageFile))
                {
                    try
                    {
                        foreach (var entry in zf)
                        {
                            entry.Extract(Directory.GetParent(Program.PackageFile).FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_progressReporter.Fail(ex))
                            _progressReporter.Terminate();
                    }
                }

                var directories = Directory.GetParent(Program.PackageFile).GetDirectories();
                int directoryContentCount = directories.Sum(
                    directory => Directory.GetFiles(directory.FullName, "*.*", SearchOption.AllDirectories).Length);

                var simpleOperations = Program.Operations.Where(item =>
                    item.Area != OperationArea.Registry &&
                    (item.Area != OperationArea.Files && item.Method != OperationMethod.Delete)).ToArray();
                _totalTaskCount = directoryContentCount + simpleOperations.Count();

                if (Program.Operations.Any() || directoryContentCount != 0)
                    _percentageMultiplier = 50f;

                foreach (var directory in directories)
                {
                    switch (directory.Name)
                    {
                        case "Program":
                            CopyDirectoryRecursively(directory.FullName, Program.AimFolder);
                            break;
                        case "AppData":
                            CopyDirectoryRecursively(directory.FullName,
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                            break;
                        case "Temp":
                            CopyDirectoryRecursively(directory.FullName, Path.GetTempPath());
                            break;
                        case "Desktop":
                            CopyDirectoryRecursively(directory.FullName,
                                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_progressReporter.Fail(ex))
                    _progressReporter.Terminate();
                return;
            }

            try
            {
                foreach (var operation in Program.Operations)
                {
                    float percentage;
                    JArray secondValueAsArray;
                    switch (operation.Area)
                    {
                        case OperationArea.Files:
                            switch (operation.Method)
                            {
                                case OperationMethod.Delete:
                                    var deleteFilePathParts = operation.Value.Split('\\');
                                    var deleteFileFullPath = Path.Combine(
                                        Operation.GetDirectory(deleteFilePathParts[0]),
                                        String.Join("\\",
                                            deleteFilePathParts.Where(item => item != deleteFilePathParts[0])));
                                    secondValueAsArray = operation.Value2 as JArray;
                                    if (secondValueAsArray != null)
                                        foreach (var fileToDelete in secondValueAsArray.ToObject<IEnumerable<string>>())
                                        {
                                            _doneTaskAmount += 1;
                                            percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                            _progressReporter.ReportOperationProgress(percentage,
                                                String.Format(Program.FileDeletingOperationText, fileToDelete));
                                            File.Delete(Path.Combine(deleteFileFullPath, fileToDelete));
                                        }
                                    break;

                                case OperationMethod.Rename:
                                    _doneTaskAmount += 1;
                                    percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                    _progressReporter.ReportOperationProgress(percentage,
                                        String.Format(Program.FileRenamingOperationText, operation.Value,
                                            operation.Value2));

                                    var renameFilePathParts = operation.Value.Split('\\');
                                    var renameFileFullPath = Path.Combine(
                                        Operation.GetDirectory(renameFilePathParts[0]),
                                        String.Join("\\",
                                            renameFilePathParts.Where(item => item != renameFilePathParts[0])));
                                    File.Move(renameFileFullPath,
                                        Path.Combine(Directory.GetParent(operation.Value).FullName,
                                            operation.Value2.ToString()));
                                    break;
                            }
                            break;
                        case OperationArea.Registry:
                            switch (operation.Method)
                            {
                                case OperationMethod.Create:
                                    secondValueAsArray = operation.Value2 as JArray;
                                    if (secondValueAsArray != null)
                                        foreach (var registryKey in secondValueAsArray.ToObject<IEnumerable<string>>())
                                        {
                                            _doneTaskAmount += 1;
                                            percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                            _progressReporter.ReportOperationProgress(percentage,
                                                String.Format(Program.RegistrySubKeyCreateOperationText, registryKey));
                                            RegistryManager.CreateSubKey(operation.Value, registryKey);
                                        }
                                    break;

                                case OperationMethod.Delete:
                                    secondValueAsArray = operation.Value2 as JArray;
                                    if (secondValueAsArray != null)
                                        foreach (var registryKey in secondValueAsArray.ToObject<IEnumerable<string>>())
                                        {
                                        _doneTaskAmount += 1;
                                        percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                        _progressReporter.ReportOperationProgress(percentage,
                                            String.Format(Program.RegistrySubKeyDeleteOperationText, registryKey));
                                        RegistryManager.DeleteSubKey(operation.Value, registryKey);
                                    }
                                    break;

                                case OperationMethod.SetValue:
                                    secondValueAsArray = operation.Value2 as JArray;
                                    if (secondValueAsArray != null)
                                        foreach (
                                            var nameValuePair in secondValueAsArray.ToObject<BindingList<Tuple<string, object, RegistryValueKind>>>())
                                        {
                                            _doneTaskAmount += 1;
                                            percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                            _progressReporter.ReportOperationProgress(percentage,
                                                String.Format(Program.RegistryNameValuePairSetValueOperationText,
                                                    nameValuePair.Item1, nameValuePair.Item2));
                                            RegistryManager.SetValue(operation.Value, nameValuePair.Item1,
                                                nameValuePair.Item2, nameValuePair.Item3);
                                        }
                                    break;

                                case OperationMethod.DeleteValue:
                                    secondValueAsArray = operation.Value2 as JArray;
                                    if (secondValueAsArray != null)
                                        foreach (var valueName in secondValueAsArray.ToObject<IEnumerable<string>>())
                                        {
                                        _doneTaskAmount += 1;
                                        percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                                        _progressReporter.ReportOperationProgress(percentage,
                                            String.Format(Program.RegistryNameValuePairSetValueOperationText, valueName));
                                        RegistryManager.DeleteValue(operation.Value, valueName);
                                    }
                                    break;
                            }
                            break;

                        case OperationArea.Processes:
                            _doneTaskAmount += 1;
                            percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                            switch (operation.Method)
                            {
                                case OperationMethod.Start:
                                    _progressReporter.ReportOperationProgress(percentage,
                                        String.Format(Program.ProcessStartOperationText, operation.Value));

                                    var processFilePathParts = operation.Value.Split('\\');
                                    var processFileFullPath =
                                        Path.Combine(Operation.GetDirectory(processFilePathParts[0]),
                                            String.Join("\\",
                                                processFilePathParts.Where(item => item != processFilePathParts[0])));

                                    var process = new Process
                                    {
                                        StartInfo =
                                        {
                                            FileName = processFileFullPath,
                                            Arguments = operation.Value2.ToString()
                                        }
                                    };
                                    process.Start();
                                    break;

                                case OperationMethod.Stop:
                                    _progressReporter.ReportOperationProgress(percentage,
                                        String.Format(Program.ProcessStopOperationText, operation.Value));

                                    var processes = Process.GetProcessesByName(operation.Value);
                                    foreach (var foundProcess in processes)
                                        foundProcess.Kill();
                                    break;
                            }
                            break;

                        case OperationArea.Services:
                            _doneTaskAmount += 1;
                            percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                            switch (operation.Method)
                            {
                                case OperationMethod.Start:
                                    _progressReporter.ReportOperationProgress(percentage,
                                        String.Format(Program.ServiceStartOperationText, operation.Value));

                                    ServiceManager.StartService(operation.Value, (string[]) operation.Value2);
                                    break;

                                case OperationMethod.Stop:
                                    _progressReporter.ReportOperationProgress(percentage,
                                        String.Format(Program.ServiceStopOperationText, operation.Value));

                                    ServiceManager.StopService(operation.Value);
                                    break;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_progressReporter.Fail(ex)) // If it should be terminated
                {
                    _progressReporter.Terminate();
                }
            }

            try
            {
                Directory.Delete(Directory.GetParent(Program.PackageFile).FullName, true);
            }
            catch (Exception ex)
            {
                _progressReporter.Fail(ex);
            }

            var p = new Process
            {
                StartInfo = {UseShellExecute = true, FileName = Program.ApplicationExecutablePath}
            };
            p.Start();

            _progressReporter.Terminate();
        }

        private void CopyDirectoryRecursively(string sourceDirName, string destDirName)
        {
            try
            {
                var dir = new DirectoryInfo(sourceDirName);
                var sourceDirectories = dir.GetDirectories();

                if (!Directory.Exists(destDirName))
                    Directory.CreateDirectory(destDirName);

                var files = dir.GetFiles();
                foreach (var file in files)
                {
                    _doneTaskAmount += 1;
                    var percentage = ((float) _doneTaskAmount/_totalTaskCount)*_percentageMultiplier;
                    _progressReporter.ReportUnpackingProgress(percentage, file.Name);

                    var aimPath = Path.Combine(destDirName, file.Name);
                    file.CopyTo(aimPath, true);
                }

                foreach (var subDirectories in sourceDirectories)
                {
                    var aimDirectoryPath = Path.Combine(destDirName, subDirectories.Name);
                    CopyDirectoryRecursively(subDirectories.FullName, aimDirectoryPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}