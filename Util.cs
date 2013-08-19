using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Xml;
using System.Text.RegularExpressions;
using System.IO;

namespace winsw
{
   
    


    // Utility Class
    class Util
    {
        /* Finger in the air estimates from Jon
           3gb RAM => 728mb java heap
           4gb RAM => 1gb java heap - although possibly up to 1.5 - needs testing
           6gb RAM => 3gb java heap
           8gb RAM => 5gb java heap
        */

        //                                           1       2    3     4          5             6         7        8         9         10          11        12
        private static long[] heapSizeNoSQLArray = new long[] { 512, 768, 768, 1024, (long)(1.5 * 1024), 3 * 1024, 4 * 1024, 5 * 1024, 6 * 1024, 7 * 1024, 8 * 1024, 9 * 1024 };

        private static long[] heapSizeSQLArray = new long[] { 512, 512, 768, 1024, (long)(1 * 1024), 2 * 1024, 3 * 1024, 4 * 1024, (long)(4.5 * 1024), 5 * 1024, 6 * 1024, (long)(6.5 * 1024) };

        /* SQL Server memory recommendations for dedicated server - we should adjust down from these?
        Physical RAM                        MaxServerMem Setting 
        2GB                                           1500 
        4GB                                           3200 
        6GB                                           4800 
        8GB                                           6400 
        12GB                                         10000 
        16GB                                         13500 
        24GB                                         21500 
        32GB                                         29000 
        48GB                                         44000 
        64GB                                         60000
        72GB                                         68000
        96GB                                         92000
        128GB                                       124000
        */


        // Gets the total available physical memory
        public static long GetTotalMemory()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return (long)memStatus.ullTotalPhys;
            }
            return -1;
        }




        public static bool IsSQLServerInstalled()
        {
            try
            {
                using (RegistryKey sqlServerKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server"))
                {
                    string[] val = (string[])sqlServerKey.GetValue("installedInstances", null);
                    return (val != null && val.Length > 0);
                }
            }
            catch (Exception ex)
            {
                // error detecting SQL
            }
            return false;
        }


        private static long GetHeapSize(double gb, long[] heapSizeArray, bool isSQL)
        {
            int index = -1 + (int)Math.Round(gb);
            if (index < 0)
                index = 0;
            long heapSize = 0;
            if (index < heapSizeArray.Length)
            {
                // get value from table
                heapSize = heapSizeArray[index];
                // calculate fractional value
                if (gb < index && index > 0)
                {
                    heapSize += (long)Math.Round((heapSizeArray[index] - heapSizeArray[index - 1]) * (gb - index + 1));
                }
                else if (index < heapSizeArray.Length - 1)
                {
                    heapSize += (long)Math.Round((heapSizeArray[index + 1] - heapSizeArray[index]) * (gb - index));
                }
                else
                    heapSize = (long)Math.Round(1024 * (isSQL ? gb / 2 : gb - 4));
            }
            else
            {
                // reserve 3gb for OS
                heapSize = (long)Math.Round(1024 * (isSQL ? gb / 2 : gb - 4));

            }
            return heapSize;
        }

        private static long CalculateJavaHeapSize()
        {
            //if (Is64BitOperatingSystem())
            {
                bool isSQL = IsSQLServerInstalled();
                long totalMemory = GetTotalMemory();
                // caculate memory expressed in GB's.
                double gb = totalMemory / 1073741824;
                // adjust available memory for SQL Server
                long[] heapSizeArray = isSQL ? heapSizeSQLArray : heapSizeNoSQLArray;

                // calc array index


                return GetHeapSize(gb, heapSizeArray, isSQL);
            }

            return -1;

        }

        public static void AdjustServiceSettings()
        {
            long heapSize = CalculateJavaHeapSize();
            if (heapSize > 0)
            {
                ServiceDescriptor descriptor = new ServiceDescriptor();
                XmlDocument doc = descriptor.document;

                XmlNode node = doc.SelectSingleNode("service/arguments");
                if (node != null)
                {
                    // args cannot be empty otherwise we've got bigger problems
                    string args = node.InnerText;
                    if (args.Contains("-Xmx"))
                    {
                        // regex replace memory value
                        args = Regex.Replace(args, @"-Xmx\d+m", "-Xmx" + heapSize.ToString() + "m");
                    }
                    else
                    {
                        // add the memory setting
                        args = "-Xmx" + heapSize.ToString() + "m " + args;
                    }
                    node.InnerText = args;

                    // save updated value
                    descriptor.Save();
                }
            }

        }


        public static void OutputMemoryTable()
        {
            using (StreamWriter outfile = new StreamWriter(@"c:\temp\table.csv"))
            {
                double gb = 0.5d;
                for (int i = 0; i < 65; i++)
                {
                    outfile.Write(String.Format("{0}, \t\t{1}, \t\t{2}\r\n", gb, GetHeapSize(gb, heapSizeNoSQLArray, false), GetHeapSize(gb, heapSizeSQLArray, true)));
                    gb += 0.5;
                }
            }
        }


        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);


        /// <summary>
        /// The function determines whether the current operating system is a 
        /// 64-bit operating system.
        /// </summary>
        /// <returns>
        /// The function returns true if the operating system is 64-bit; 
        /// otherwise, it returns false.
        /// </returns>
        public static bool Is64BitOperatingSystem()
        {
            if (IntPtr.Size == 8)  // 64-bit programs run only on Win64
            {
                return true;
            }
            else  // 32-bit programs run on both 32-bit and 64-bit Windows
            {
                // Detect whether the current process is a 32-bit process 
                // running on a 64-bit system.
                bool flag;
                return ((DoesWin32MethodExist("kernel32.dll", "IsWow64Process") &&
                    IsWow64Process(GetCurrentProcess(), out flag)) && flag);
            }
        }

        /// <summary>
        /// The function determins whether a method exists in the export 
        /// table of a certain module.
        /// </summary>
        /// <param name="moduleName">The name of the module</param>
        /// <param name="methodName">The name of the method</param>
        /// <returns>
        /// The function returns true if the method specified by methodName 
        /// exists in the export table of the module specified by moduleName.
        /// </returns>
        static bool DoesWin32MethodExist(string moduleName, string methodName)
        {
            IntPtr moduleHandle = GetModuleHandle(moduleName);
            if (moduleHandle == IntPtr.Zero)
            {
                return false;
            }
            return (GetProcAddress(moduleHandle, methodName) != IntPtr.Zero);
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule,
            [MarshalAs(UnmanagedType.LPStr)]string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);




        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

    }
}
