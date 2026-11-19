using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace BackupService
{
    // Программа управления службой (SCP): установка, удаление, запуск, остановка через функции SCM.
    internal static class ServiceControl
    {
        private const int WaitTimeoutMs = 60000;

        // OpenSCManager + CreateService: регистрация службы в SCM
        // (SCM создаёт раздел HKLM\SYSTEM\CurrentControlSet\Services\<имя службы>)
        public static int Install()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;

            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CREATE_SERVICE);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = NativeMethods.CreateService(
                    scm,
                    Program.ServiceName,
                    Program.DisplayName,
                    NativeMethods.SERVICE_ALL_ACCESS,
                    NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                    NativeMethods.SERVICE_DEMAND_START,     // запуск вручную
                    NativeMethods.SERVICE_ERROR_NORMAL,
                    "\"" + exePath + "\"",                   // путь в кавычках: в нём могут быть пробелы
                    null, IntPtr.Zero, null,
                    null, null);                             // учётная запись LocalSystem
                if (service == IntPtr.Zero)
                    return Fail("CreateService", Marshal.GetLastWin32Error());

                var description = new NativeMethods.SERVICE_DESCRIPTION { lpDescription = Program.Description };
                if (!NativeMethods.ChangeServiceConfig2(service, NativeMethods.SERVICE_CONFIG_DESCRIPTION, ref description))
                    Warn("ChangeServiceConfig2", Marshal.GetLastWin32Error());

                NativeMethods.CloseServiceHandle(service);
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }

            Console.WriteLine("Служба {0} (\"{1}\") установлена", Program.ServiceName, Program.DisplayName);
            Console.WriteLine("  Исполняемый файл: {0}", exePath);
            Console.WriteLine("  Конфигурация:     {0}", Config.DefaultPath);
            Console.WriteLine("  Журнал:           {0}", Log.FilePath);
            return 0;
        }

        // OpenService + DeleteService. Работающая служба сначала останавливается.
        public static int Remove()
        {
            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = OpenService(scm, NativeMethods.DELETE | NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                    return 1;
                try
                {
                    NativeMethods.SERVICE_STATUS_PROCESS st;
                    if (QueryStatus(service, out st) && st.dwCurrentState != NativeMethods.SERVICE_STOPPED)
                    {
                        Console.WriteLine("Служба работает, останавливаю...");
                        if (StopAndWait(service) != 0)
                            return 1;
                    }

                    if (!NativeMethods.DeleteService(service))
                        return Fail("DeleteService", Marshal.GetLastWin32Error());
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }

            Console.WriteLine("Служба {0} удалена", Program.ServiceName);
            Console.WriteLine("  Если оснастка \"Службы\" открыта, обновите список (F5).");
            return 0;
        }

        // OpenService + StartService, затем ожидание состояния RUNNING
        public static int Start()
        {
            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = OpenService(scm, NativeMethods.SERVICE_START | NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                    return 1;
                try
                {
                    return StartAndWait(service);
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }
        }

        // OpenService + ControlService(SERVICE_CONTROL_STOP), затем ожидание состояния STOPPED
        public static int Stop()
        {
            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = OpenService(scm, NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                    return 1;
                try
                {
                    return StopAndWait(service);
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }
        }

        public static int Restart()
        {
            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = OpenService(scm, NativeMethods.SERVICE_START | NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                    return 1;
                try
                {
                    NativeMethods.SERVICE_STATUS_PROCESS st;
                    if (QueryStatus(service, out st) && st.dwCurrentState != NativeMethods.SERVICE_STOPPED
                        && StopAndWait(service) != 0)
                        return 1;
                    return StartAndWait(service);
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }
        }

        // QueryServiceStatusEx: текущее состояние службы
        public static int Status()
        {
            IntPtr scm = OpenScm(NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return 1;
            try
            {
                IntPtr service = NativeMethods.OpenService(scm, Program.ServiceName, NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        Console.WriteLine("Служба {0} не установлена", Program.ServiceName);
                        return 0;
                    }
                    return Fail("OpenService", error);
                }
                try
                {
                    NativeMethods.SERVICE_STATUS_PROCESS st;
                    if (!QueryStatus(service, out st))
                        return Fail("QueryServiceStatusEx", Marshal.GetLastWin32Error());

                    Console.WriteLine("Служба {0}: {1}", Program.ServiceName, StateName(st.dwCurrentState));
                    if (st.dwProcessId != 0)
                        Console.WriteLine("  PID процесса:     {0}", st.dwProcessId);
                    if (st.dwCurrentState == NativeMethods.SERVICE_STOPPED && st.dwWin32ExitCode != NativeMethods.NO_ERROR)
                        PrintExitCode(st);
                    Console.WriteLine("  Конфигурация:     {0}", Config.DefaultPath);
                    Console.WriteLine("  Журнал:           {0}", Log.FilePath);
                    return 0;
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scm);
            }
        }

        // ---- Вспомогательные функции ----

        private static int StartAndWait(IntPtr service)
        {
            if (!NativeMethods.StartService(service, 0, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_SERVICE_ALREADY_RUNNING)
                {
                    Console.WriteLine("Служба {0} уже запущена", Program.ServiceName);
                    return 0;
                }
                return Fail("StartService", error);
            }

            Console.WriteLine("Запуск службы {0}...", Program.ServiceName);
            NativeMethods.SERVICE_STATUS_PROCESS st;
            if (!WaitWhilePending(service, NativeMethods.SERVICE_START_PENDING, out st))
                return 1;

            if (st.dwCurrentState == NativeMethods.SERVICE_RUNNING)
            {
                Console.WriteLine("Служба {0} запущена (PID {1})", Program.ServiceName, st.dwProcessId);
                return 0;
            }
            Console.WriteLine("Служба не запустилась, состояние: {0}", StateName(st.dwCurrentState));
            PrintExitCode(st);
            Console.WriteLine("  Подробности в журнале: {0}", Log.FilePath);
            return 1;
        }

        private static int StopAndWait(IntPtr service)
        {
            var ss = new NativeMethods.SERVICE_STATUS();
            if (!NativeMethods.ControlService(service, NativeMethods.SERVICE_CONTROL_STOP, ref ss))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_SERVICE_NOT_ACTIVE)
                {
                    Console.WriteLine("Служба {0} не запущена", Program.ServiceName);
                    return 0;
                }
                return Fail("ControlService(STOP)", error);
            }

            Console.WriteLine("Остановка службы {0}...", Program.ServiceName);
            NativeMethods.SERVICE_STATUS_PROCESS st;
            if (!WaitWhilePending(service, NativeMethods.SERVICE_STOP_PENDING, out st))
                return 1;
            if (st.dwCurrentState != NativeMethods.SERVICE_STOPPED)
            {
                Console.WriteLine("Служба не остановилась, состояние: {0}", StateName(st.dwCurrentState));
                return 1;
            }
            Console.WriteLine("Служба {0} остановлена", Program.ServiceName);
            return 0;
        }

        // Ждём, пока служба находится в состоянии pendingState (START_PENDING / STOP_PENDING)
        private static bool WaitWhilePending(IntPtr service, int pendingState, out NativeMethods.SERVICE_STATUS_PROCESS st)
        {
            int waited = 0;
            while (true)
            {
                if (!QueryStatus(service, out st))
                {
                    Fail("QueryServiceStatusEx", Marshal.GetLastWin32Error());
                    return false;
                }
                if (st.dwCurrentState != pendingState)
                    return true;
                if (waited >= WaitTimeoutMs)
                {
                    Console.WriteLine("Служба не ответила за {0} с", WaitTimeoutMs / 1000);
                    return false;
                }
                Thread.Sleep(250);
                waited += 250;
            }
        }

        private static bool QueryStatus(IntPtr service, out NativeMethods.SERVICE_STATUS_PROCESS st)
        {
            st = new NativeMethods.SERVICE_STATUS_PROCESS();
            int needed;
            return NativeMethods.QueryServiceStatusEx(service, NativeMethods.SC_STATUS_PROCESS_INFO, ref st,
                Marshal.SizeOf(typeof(NativeMethods.SERVICE_STATUS_PROCESS)), out needed);
        }

        private static IntPtr OpenScm(int access)
        {
            IntPtr scm = NativeMethods.OpenSCManager(null, null, access);
            if (scm == IntPtr.Zero)
                Fail("OpenSCManager", Marshal.GetLastWin32Error());
            return scm;
        }

        private static IntPtr OpenService(IntPtr scm, int access)
        {
            IntPtr service = NativeMethods.OpenService(scm, Program.ServiceName, access);
            if (service == IntPtr.Zero)
                Fail("OpenService", Marshal.GetLastWin32Error());
            return service;
        }

        private static void PrintExitCode(NativeMethods.SERVICE_STATUS_PROCESS st)
        {
            if (st.dwWin32ExitCode == NativeMethods.ERROR_SERVICE_SPECIFIC_ERROR)
                Console.WriteLine("  Служба завершилась с ошибкой, код службы: {0} ({1})",
                    st.dwServiceSpecificExitCode, Program.ExitCodeText(st.dwServiceSpecificExitCode));
            else if (st.dwWin32ExitCode != NativeMethods.NO_ERROR)
                Console.WriteLine("  Код завершения: {0} - {1}", st.dwWin32ExitCode, ScmErrors.Text(st.dwWin32ExitCode));
        }

        private static int Fail(string function, int error)
        {
            Console.WriteLine("Ошибка {0}: {1} - {2}", function, error, ScmErrors.Text(error));
            string hint = ScmErrors.Hint(error);
            if (hint != null)
                Console.WriteLine("  " + hint);
            return 1;
        }

        private static void Warn(string function, int error)
        {
            Console.WriteLine("Предупреждение {0}: {1} - {2}", function, error, ScmErrors.Text(error));
        }

        public static string StateName(int state)
        {
            switch (state)
            {
                case NativeMethods.SERVICE_STOPPED:          return "остановлена";
                case NativeMethods.SERVICE_START_PENDING:    return "запускается";
                case NativeMethods.SERVICE_STOP_PENDING:     return "останавливается";
                case NativeMethods.SERVICE_RUNNING:          return "выполняется";
                case NativeMethods.SERVICE_CONTINUE_PENDING: return "возобновляется";
                case NativeMethods.SERVICE_PAUSE_PENDING:    return "приостанавливается";
                case NativeMethods.SERVICE_PAUSED:           return "приостановлена";
                default:                                     return "состояние " + state;
            }
        }
    }

    internal static class ScmErrors
    {
        // Текст системной ошибки по коду Win32
        public static string Text(int error)
        {
            return new Win32Exception(error).Message;
        }

        public static string Hint(int error)
        {
            switch (error)
            {
                case NativeMethods.ERROR_ACCESS_DENIED:
                    return "Запустите командную строку от имени администратора.";
                case NativeMethods.ERROR_SERVICE_EXISTS:
                    return "Служба уже установлена. Для переустановки выполните remove, затем install.";
                case NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST:
                    return "Служба не установлена. Выполните install.";
                case NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE:
                    return "Служба помечена для удаления. Закройте оснастку \"Службы\" и повторите.";
                case NativeMethods.ERROR_SERVICE_CANNOT_ACCEPT_CTRL:
                    return "Служба сейчас запускается или останавливается, повторите позже.";
                default:
                    return null;
            }
        }
    }
}
