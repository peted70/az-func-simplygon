// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license. 

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Simplygon
{
    public class Loader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetDllDirectory(string lpPathName);

        public static ISimplygon InitSimplygon(out EErrorCodes errorCode, out string errorMessage)
        {
            string sdkPath = GetSDKPath();

            errorCode = 0;
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(sdkPath))
            {
                errorCode = EErrorCodes.DLLOrDependenciesNotFound;
                errorMessage = "Simplygon.dll not found";
                return null;
            }

            try
            {
                var simplygon = Simplygon.InitializeSimplygon(sdkPath);
                errorCode = Simplygon.GetLastInitializationError();
                if (errorCode != EErrorCodes.NoError)
                {
                    throw new Exception(string.Format("Failed to load Simplygon from {0}\nErrorCode: {1}", sdkPath, errorCode));
                }
                return simplygon;
            }
            catch (NotSupportedException ex)
            {
                errorCode = EErrorCodes.AlreadyInitialized;

                string exceptionMessage = string.Format($"Failed to load Simplygon from {sdkPath}\nErrorCode: {errorCode}\nMessage: {ex.Message}");
                Console.Error.WriteLine(exceptionMessage);

                errorMessage = exceptionMessage;
            }
            catch (SEHException ex)
            {
                string exceptionMessage = string.Format($"Failed to load Simplygon from {sdkPath}\nErrorCode: {errorCode}\nMessage: {ex.Message}");
                Console.Error.WriteLine(exceptionMessage);

                errorCode = EErrorCodes.DLLOrDependenciesNotFound;
                errorMessage = exceptionMessage;
            }
            catch (Exception ex)
            {
                errorCode = EErrorCodes.DLLOrDependenciesNotFound;
                errorMessage = ex.Message;
                Console.Error.WriteLine(errorMessage);
            }
            finally
            {
                if (errorCode != 0)
                {
                    string errorMessageEx = string.Format("Failed to load Simplygon from {0}\nErrorCode: {1}", sdkPath, errorCode);
                    Console.Error.WriteLine(errorMessageEx);
                }
            }
            return null;
        }

        private static string GetSDKPath()
        {
            string simplygon9Path = Environment.GetEnvironmentVariable("SIMPLYGON_9_PATH");

            if (string.IsNullOrEmpty(simplygon9Path))
            {
                return string.Empty;
            }

            simplygon9Path = Environment.ExpandEnvironmentVariables(simplygon9Path);

            SetDllDirectory(simplygon9Path);

            string simplygonDLLPath = Path.Combine(simplygon9Path, "Simplygon.dll");
            if (File.Exists(simplygonDLLPath))
            {
                return simplygonDLLPath;
            }

            return string.Empty;
        }
    }
}
