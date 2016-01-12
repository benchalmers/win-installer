﻿using Microsoft.Win32;
using PInvokeWrap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace XSToolsInstallation
{
    public static class Device
    {
        // Use to create a list of strings from the
        // "byte[] propertyBuffer" variable returned
        // by SetupDiGetDeviceRegistryProperty()
        private static string[] MultiByteStringSplit(byte[] mbstr)
        {
            List<string> strList = new List<string>();
            int strStart = 0;

            // One character is represented by 2 bytes.
            for (int i = 0; i < mbstr.Length; i += 2)
            {
                if (mbstr[i] == '\0')
                {
                    strList.Add(
                        System.Text.Encoding.Unicode.GetString(
                            mbstr,
                            strStart,
                            i - strStart
                        )
                    );

                    strStart = i + 2;

                    if (strStart < mbstr.Length && mbstr[strStart] == '\0')
                    {
                        break;
                    }
                }
            }

            return strList.ToArray();
        }

        public static string GetDevRegPropertyStr(
            SetupApi.DeviceInfoSet devInfoSet,
            SetupApi.SP_DEVINFO_DATA devInfoData,
            SetupApi.SPDRP property)
        // Use this function for any 'Device Registry
        // Property' that returns a string,
        // e.g. SPDRP_CLASSGUID
        {
            int propertyRegDataType;
            int requiredSize;

            // 'buffer' is 1KB  but Unicode chars are 2 bytes,
            // hence 'buffer' can hold up to 512 chars
            const int BUFFER_SIZE = 1024;
            byte[] buffer = new byte[BUFFER_SIZE];

            SetupApi.SetupDiGetDeviceRegistryProperty(
                devInfoSet.Get(),
                devInfoData,
                property,
                out propertyRegDataType,
                buffer,
                BUFFER_SIZE,
                out requiredSize
            );

            return System.Text.Encoding.Unicode.GetString(
                buffer,
                0,
                requiredSize
            );
        }

        public static string[] GetDevRegPropertyMultiStr(
            SetupApi.DeviceInfoSet devInfoSet,
            SetupApi.SP_DEVINFO_DATA devInfoData,
            SetupApi.SPDRP property)
        // Use this function for any 'Device Registry
        // Property' that returns 'REG_MULTI_SZ',
        // e.g. SPDRP_HARDWAREID
        {
            int propertyRegDataType;
            int requiredSize;

            // 'buffer' is 4KB  but Unicode chars are 2 bytes,
            // hence 'buffer' can hold up to 2K chars
            const int BUFFER_SIZE = 4096;
            byte[] buffer = new byte[BUFFER_SIZE];

            SetupApi.SetupDiGetDeviceRegistryProperty(
                devInfoSet.Get(),
                devInfoData,
                property,
                out propertyRegDataType,
                buffer,
                BUFFER_SIZE,
                out requiredSize
            );

            return MultiByteStringSplit(buffer);
        }

        public static string GetDriverVersion(
            SetupApi.DeviceInfoSet devInfoSet,
            SetupApi.SP_DEVINFO_DATA devInfoData)
        {
            string driverKeyName = GetDevRegPropertyStr(
                devInfoSet,
                devInfoData,
                SetupApi.SPDRP.DRIVER
            );

            if (String.IsNullOrEmpty(driverKeyName))
            {
                return "0.0.0.0";
            }

            driverKeyName =
                @"SYSTEM\CurrentControlSet\Control\Class\" + driverKeyName;

            RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                driverKeyName, true
            );
            
            return (string)rk.GetValue("DriverVersion");
        }


        public static SetupApi.SP_DEVINFO_DATA FindInSystem(
            string hwID,
            SetupApi.DeviceInfoSet devInfoSet,
            bool strictSearch)
        // The function takes as input an initialized 'deviceInfoSet'
        // object and a hardware ID string we want to search the system
        // for. If 'strictSearch' is true, the device needs to exactly
        // match the hwID to be returned. Otherwise, the device's name
        // needs to start with the supplied hwID string. If the device
        // is found, a fully initialized 'SP_DEVINFO_DATA' object is
        // returned. If not, the function returns 'null'.
        {
            SetupApi.SP_DEVINFO_DATA devInfoData =
                new SetupApi.SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            // Select which string comparison function
            // to use, depending on 'strictSearch'
            Func<string, string, bool> hwIDFound;
            if (strictSearch)
            {
                hwIDFound = (string _enumID, string _hwID) =>
                    _enumID.Equals(
                        _hwID,
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            else
            {
                hwIDFound = (string _enumID, string _hwID) =>
                    _enumID.StartsWith(
                        _hwID,
                        StringComparison.OrdinalIgnoreCase
                    );
            }

            Trace.WriteLine(
                "Searching system for device: \'" + hwID +
                "\'; (strict search: \'" + strictSearch + "\')"
            );

            for (uint i = 0;
                 SetupApi.SetupDiEnumDeviceInfo(
                     devInfoSet.Get(),
                     i,
                     devInfoData);
                 ++i)
            {
                string [] ids = GetDevRegPropertyMultiStr(
                    devInfoSet,
                    devInfoData,
                    SetupApi.SPDRP.HARDWAREID
                );

                foreach (string id in ids)
                {
                    if (hwIDFound(id, hwID))
                    {
                        Trace.WriteLine(
                            "Found: \'" + String.Join("  ", ids) + "\'"
                        );
                        return devInfoData;
                    }
                }
            }

            Win32Error.Set("SetupDiEnumDeviceInfo");
            if (Win32Error.GetErrorNo() == 259) // ERROR_NO_MORE_ITEMS
            {
                Trace.WriteLine("Device not found");
                return null;
            }

            Trace.WriteLine(Win32Error.GetFullErrMsg());
            throw new Exception(Win32Error.GetFullErrMsg());
        }

        public static void RemoveFromSystem(
            SetupApi.DeviceInfoSet devInfoSet,
            string hwID,
            bool strictSearch)
        {
            if (!devInfoSet.HandleIsValid())
            {
                return;
            }

            SetupApi.SP_DEVINFO_DATA devInfoData;

            devInfoData = FindInSystem(
                hwID,
                devInfoSet,
                strictSearch
            );

            if (devInfoData == null)
            {
                return;
            }

            SetupApi.SP_REMOVEDEVICE_PARAMS rparams =
                new SetupApi.SP_REMOVEDEVICE_PARAMS();

            rparams.ClassInstallHeader.cbSize =
                (uint)Marshal.SizeOf(rparams.ClassInstallHeader);

            rparams.ClassInstallHeader.InstallFunction =
                SetupApi.DI_FUNCTION.DIF_REMOVE;

            rparams.HwProfile = 0;
            rparams.Scope = SetupApi.DI_REMOVE_DEVICE_GLOBAL;
            GCHandle handle1 = GCHandle.Alloc(rparams);

            if (!SetupApi.SetupDiSetClassInstallParams(
                    devInfoSet.Get(),
                    devInfoData,
                    ref rparams,
                    Marshal.SizeOf(rparams)))
            {
                Win32Error.Set("SetupDiSetClassInstallParams");
                Trace.WriteLine(Win32Error.GetFullErrMsg());

                // TODO: write custom exception
                throw new Exception(
                    Win32Error.GetFullErrMsg()
                );
            }

            Trace.WriteLine(
                "Removing device \'" + hwID +
                "\' from system"
            );

            if (!SetupApi.SetupDiCallClassInstaller(
                    SetupApi.DI_FUNCTION.DIF_REMOVE,
                    devInfoSet.Get(),
                    devInfoData))
            {
                Win32Error.Set("SetupDiCallClassInstaller");
                Trace.WriteLine(Win32Error.GetFullErrMsg());

                // TODO: write custom exception
                throw new Exception(
                    Win32Error.GetFullErrMsg()
                );
            }

            Trace.WriteLine("Remove should have worked");
        }

        public static bool ChildrenInstalled(string enumName)
        {
            UInt32 devStatus;
            UInt32 devProblemCode;

            SetupApi.SP_DEVINFO_DATA devInfoData =
                new SetupApi.SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

            using (SetupApi.DeviceInfoSet devInfoSet =
                       new SetupApi.DeviceInfoSet(
                       IntPtr.Zero,
                       enumName,
                       IntPtr.Zero,
                       SetupApi.DiGetClassFlags.DIGCF_PRESENT |
                       SetupApi.DiGetClassFlags.DIGCF_ALLCLASSES))
            {
                for (uint i = 0;
                     SetupApi.SetupDiEnumDeviceInfo(
                         devInfoSet.Get(),
                         i,
                         devInfoData);
                     ++i)
                {
                    SetupApi.CM_Get_DevNode_Status(
                        out devStatus,
                        out devProblemCode,
                        devInfoData.devInst,
                        0
                    );

                    if ((devStatus & (uint)SetupApi.DNFlags.DN_STARTED) == 0)
                    {
                        Trace.WriteLine(
                            enumName +
                            " child not started " +
                            devStatus.ToString()
                        );

                        return false;
                    }
                }
            }
            return true;
        }

        public static string GetDeviceInstanceId(string enum_device)
        // Returns the device instance ID of the specified device
        // 'enum_device' should have the following format:
        // <enumerator>\<device_id>
        {
            const int BUFFER_SIZE = 4096;
            string enumerator = enum_device.Split(new char[] { '\\' })[0];
            StringBuilder deviceInstanceId = new StringBuilder(BUFFER_SIZE);
            SetupApi.SP_DEVINFO_DATA devInfoData;
            int reqSize;

            using (SetupApi.DeviceInfoSet devInfoSet =
                new SetupApi.DeviceInfoSet(
                    IntPtr.Zero,
                    enumerator,
                    IntPtr.Zero,
                    SetupApi.DiGetClassFlags.DIGCF_ALLCLASSES |
                    SetupApi.DiGetClassFlags.DIGCF_PRESENT))
            {
                devInfoData = Device.FindInSystem(
                    enum_device,
                    devInfoSet,
                    false
                );

                if (devInfoData == null)
                {
                    return "";
                }

                if (!SetupApi.SetupDiGetDeviceInstanceId(
                        devInfoSet.Get(),
                        devInfoData,
                        deviceInstanceId,
                        BUFFER_SIZE,
                        out reqSize))
                {
                    Win32Error.Set("SetupDiGetDeviceInstanceId");
                    throw new Exception(Win32Error.GetFullErrMsg());
                }
            }

            return deviceInstanceId.ToString();
        }

        public static int GetDevNode(string enum_device = "")
        // Returns the device node of the specified device
        // 'enum_device' should have the following format:
        // <enumerator>\<device_id>
        // If it is the empty string, the root of the device
        // tree will be returned
        {
            SetupApi.CR err;
            int devNode;
            string deviceInstanceId;

            if (!String.IsNullOrEmpty(enum_device))
            {
                deviceInstanceId = GetDeviceInstanceId(enum_device);

                if (String.IsNullOrEmpty(deviceInstanceId))
                {
                    Trace.WriteLine("No instance exists in system");
                    return -1;
                }
            }
            else
            {
                deviceInstanceId = "";
            }

            err = SetupApi.CM_Locate_DevNode(
                out devNode,
                deviceInstanceId,
                SetupApi.CM_LOCATE_DEVNODE.NORMAL
            );

            if (err != SetupApi.CR.SUCCESS)
            {
                Trace.WriteLine("CM_Locate_DevNode() error: " + err);
                return -1;
            }

            return devNode;
        }

        public static bool Enumerate(
            string enum_device = "",
            bool installDevices = false)
        // 'enum_device' should have the following format:
        // <enumerator>\<device_id>
        // If it is the empty string, the root of the device
        // tree will be enumerated
        // If 'installDevices' is 'true', PnP will try to complete
        // installation of any not-fully-installed devices.
        {
            SetupApi.CR err;
            int devNode = GetDevNode(enum_device);
            SetupApi.CM_REENUMERATE ulFlags = SetupApi.CM_REENUMERATE.NORMAL;

            if (installDevices)
            {
                ulFlags |= SetupApi.CM_REENUMERATE.RETRY_INSTALLATION;
            }

            if (devNode == -1)
            {
                Trace.WriteLine("Could not get DevNode");
                return false;
            }

            Helpers.AcquireSystemPrivilege(AdvApi32.SE_LOAD_DRIVER_NAME);

            err = SetupApi.CM_Reenumerate_DevNode(devNode, ulFlags);

            if (err != SetupApi.CR.SUCCESS)
            {
                Trace.WriteLine("CM_Reenumerate_DevNode() error: " + err);
                return false;
            }

            Trace.WriteLine("Enumeration completed successfully");
            return true;
        }
    }
}
