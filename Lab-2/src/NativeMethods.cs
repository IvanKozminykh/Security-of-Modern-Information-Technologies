using System;
using System.Runtime.InteropServices;

namespace BackupService
{
    // Объявления функций и констант Win32 API для работы с SCM (advapi32.dll).
    internal static class NativeMethods
    {
        // Права доступа к SCM и к службе
        public const int SC_MANAGER_CONNECT = 0x0001;
        public const int SC_MANAGER_CREATE_SERVICE = 0x0002;
        public const int SERVICE_QUERY_STATUS = 0x0004;
        public const int SERVICE_START = 0x0010;
        public const int SERVICE_STOP = 0x0020;
        public const int DELETE = 0x00010000;
        public const int SERVICE_ALL_ACCESS = 0x000F01FF;

        // Тип службы, тип запуска, реакция на ошибку запуска
        public const int SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        public const int SERVICE_DEMAND_START = 0x00000003;
        public const int SERVICE_ERROR_NORMAL = 0x00000001;

        // Состояния службы
        public const int SERVICE_STOPPED = 1;
        public const int SERVICE_START_PENDING = 2;
        public const int SERVICE_STOP_PENDING = 3;
        public const int SERVICE_RUNNING = 4;
        public const int SERVICE_CONTINUE_PENDING = 5;
        public const int SERVICE_PAUSE_PENDING = 6;
        public const int SERVICE_PAUSED = 7;

        // Команды, которые служба готова принимать
        public const int SERVICE_ACCEPT_STOP = 0x01;
        public const int SERVICE_ACCEPT_PAUSE_CONTINUE = 0x02;
        public const int SERVICE_ACCEPT_SHUTDOWN = 0x04;

        // Коды команд от SCM
        public const int SERVICE_CONTROL_STOP = 1;
        public const int SERVICE_CONTROL_PAUSE = 2;
        public const int SERVICE_CONTROL_CONTINUE = 3;
        public const int SERVICE_CONTROL_INTERROGATE = 4;
        public const int SERVICE_CONTROL_SHUTDOWN = 5;

        public const int SERVICE_CONFIG_DESCRIPTION = 1;
        public const int SC_STATUS_PROCESS_INFO = 0;

        // Коды ошибок
        public const int NO_ERROR = 0;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_CALL_NOT_IMPLEMENTED = 120;
        public const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
        public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        public const int ERROR_SERVICE_CANNOT_ACCEPT_CTRL = 1061;
        public const int ERROR_SERVICE_NOT_ACTIVE = 1062;
        public const int ERROR_FAILED_SERVICE_CONTROLLER_CONNECT = 1063;
        public const int ERROR_SERVICE_SPECIFIC_ERROR = 1066;
        public const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
        public const int ERROR_SERVICE_EXISTS = 1073;

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS_PROCESS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
            public int dwProcessId;
            public int dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        // Точка входа службы (ServiceMain) и обработчик команд SCM (HandlerEx)
        public delegate void ServiceMainProc(int argc, IntPtr argv);
        public delegate int HandlerEx(int control, int eventType, IntPtr eventData, IntPtr context);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_TABLE_ENTRY
        {
            public string lpServiceName;
            public ServiceMainProc lpServiceProc;
        }

        // ---- Функции сервисного приложения ----

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool StartServiceCtrlDispatcher(SERVICE_TABLE_ENTRY[] serviceTable);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerEx handler, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool SetServiceStatus(IntPtr statusHandle, ref SERVICE_STATUS status);

        // ---- Функции программы управления службой (SCP) ----

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenSCManager(string machineName, string databaseName, int desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateService(IntPtr scm, string serviceName, string displayName,
            int desiredAccess, int serviceType, int startType, int errorControl, string binaryPathName,
            string loadOrderGroup, IntPtr tagId, string dependencies, string serviceStartName, string password);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenService(IntPtr scm, string serviceName, int desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref SERVICE_DESCRIPTION info);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool StartService(IntPtr service, int numArgs, IntPtr argVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool ControlService(IntPtr service, int control, ref SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel,
            ref SERVICE_STATUS_PROCESS buffer, int bufSize, out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DeleteService(IntPtr service);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CloseServiceHandle(IntPtr handle);
    }
}
