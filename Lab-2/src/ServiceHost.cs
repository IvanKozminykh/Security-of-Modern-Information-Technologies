using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BackupService
{
    // Сервисное приложение: связь с SCM, точка входа службы ServiceMain и обработчик команд SCM.
    internal static class ServiceHost
    {
        // Делегаты передаются в неуправляемый код (SCM вызывает их по указателю), поэтому ссылки на них
        // хранятся в статических полях — иначе сборщик мусора может их удалить, пока служба работает.
        private static readonly NativeMethods.ServiceMainProc MainProc = ServiceMain;
        private static readonly NativeMethods.HandlerEx HandlerProc = ControlHandler;

        private static readonly object StatusLock = new object();
        private static NativeMethods.SERVICE_STATUS status;
        private static IntPtr statusHandle;
        private static volatile BackupEngine engine;
        private static Timer pendingTimer;

        // Запуск в режиме службы: процесс запущен SCM-ом. Возвращает false, если процесс запущен
        // не SCM-ом (например, из командной строки без аргументов).
        public static bool RunAsService()
        {
            var table = new[]
            {
                new NativeMethods.SERVICE_TABLE_ENTRY { lpServiceName = Program.ServiceName, lpServiceProc = MainProc },
                new NativeMethods.SERVICE_TABLE_ENTRY()   // завершающая пустая запись
            };

            // Связывает главный поток процесса с SCM. Возвращает управление, когда служба остановлена.
            if (NativeMethods.StartServiceCtrlDispatcher(table))
                return true;

            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ERROR_FAILED_SERVICE_CONTROLLER_CONNECT)
                Log.Error("StartServiceCtrlDispatcher: ошибка {0} - {1}", error, ScmErrors.Text(error));
            return false;
        }

        // Точка входа службы. SCM вызывает её в отдельном потоке после StartService.
        private static void ServiceMain(int argc, IntPtr argv)
        {
            statusHandle = NativeMethods.RegisterServiceCtrlHandlerEx(Program.ServiceName, HandlerProc, IntPtr.Zero);
            if (statusHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Error("RegisterServiceCtrlHandlerEx: ошибка {0} - {1}", error, ScmErrors.Text(error));
                return;
            }

            status.dwServiceType = NativeMethods.SERVICE_WIN32_OWN_PROCESS;
            SetState(NativeMethods.SERVICE_START_PENDING, 10000);
            Log.Info("===== Служба запускается (PID {0}) =====", System.Diagnostics.Process.GetCurrentProcess().Id);

            Config cfg;
            try
            {
                cfg = Config.Load(Config.DefaultPath);
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка конфигурации: {0}", ex.Message);
                ReportStopped(1);
                return;
            }

            engine = new BackupEngine(cfg);
            SetState(NativeMethods.SERVICE_RUNNING, 0);
            Log.Info("Служба запущена");

            int exitCode = 0;
            try
            {
                engine.Run();   // работает, пока не придёт команда Stop/Shutdown
            }
            catch (Exception ex)
            {
                Log.Error("Аварийное завершение: {0}", ex);
                exitCode = 2;
            }
            engine.Dispose();

            if (pendingTimer != null)
                pendingTimer.Dispose();
            Log.Info("===== Служба остановлена =====");
            ReportStopped(exitCode);
        }

        // Обработчик команд, которые SCM передаёт службе (в том числе из оснастки "Службы").
        // Вызывается в главном потоке процесса (в потоке StartServiceCtrlDispatcher).
        private static int ControlHandler(int control, int eventType, IntPtr eventData, IntPtr context)
        {
            BackupEngine e = engine;
            switch (control)
            {
                case NativeMethods.SERVICE_CONTROL_STOP:
                case NativeMethods.SERVICE_CONTROL_SHUTDOWN:
                    Log.Info(control == NativeMethods.SERVICE_CONTROL_STOP
                        ? "Получена команда Stop" : "Получена команда Shutdown (выключение компьютера)");
                    SetState(NativeMethods.SERVICE_STOP_PENDING, 5000);
                    // Пока завершается текущее обновление архива, сообщаем SCM, что остановка продолжается
                    pendingTimer = new Timer(_ => BumpCheckPoint(), null, 1000, 1000);
                    if (e != null)
                        e.RequestStop();
                    return NativeMethods.NO_ERROR;

                case NativeMethods.SERVICE_CONTROL_PAUSE:
                    Log.Info("Получена команда Pause");
                    SetState(NativeMethods.SERVICE_PAUSE_PENDING, 10000);
                    if (e != null)
                        e.Pause();
                    SetState(NativeMethods.SERVICE_PAUSED, 0);
                    return NativeMethods.NO_ERROR;

                case NativeMethods.SERVICE_CONTROL_CONTINUE:
                    Log.Info("Получена команда Continue");
                    SetState(NativeMethods.SERVICE_CONTINUE_PENDING, 5000);
                    if (e != null)
                        e.Resume();
                    SetState(NativeMethods.SERVICE_RUNNING, 0);
                    return NativeMethods.NO_ERROR;

                case NativeMethods.SERVICE_CONTROL_INTERROGATE:
                    return NativeMethods.NO_ERROR;   // SCM вернёт последнее сообщённое состояние

                default:
                    return NativeMethods.ERROR_CALL_NOT_IMPLEMENTED;
            }
        }

        // Сообщить SCM новое состояние службы
        private static void SetState(int state, int waitHint)
        {
            lock (StatusLock)
            {
                bool pending = state == NativeMethods.SERVICE_START_PENDING || state == NativeMethods.SERVICE_STOP_PENDING
                            || state == NativeMethods.SERVICE_PAUSE_PENDING || state == NativeMethods.SERVICE_CONTINUE_PENDING;

                // Во время запуска и остановки команды не принимаются
                if (state == NativeMethods.SERVICE_START_PENDING || state == NativeMethods.SERVICE_STOP_PENDING
                    || state == NativeMethods.SERVICE_STOPPED)
                    status.dwControlsAccepted = 0;
                else
                    status.dwControlsAccepted = NativeMethods.SERVICE_ACCEPT_STOP
                                              | NativeMethods.SERVICE_ACCEPT_PAUSE_CONTINUE
                                              | NativeMethods.SERVICE_ACCEPT_SHUTDOWN;

                status.dwCheckPoint = pending ? (status.dwCurrentState == state ? status.dwCheckPoint + 1 : 1) : 0;
                status.dwCurrentState = state;
                status.dwWaitHint = waitHint;
                NativeMethods.SetServiceStatus(statusHandle, ref status);
            }
        }

        // Увеличить контрольную точку: служба жива и продолжает останавливаться
        private static void BumpCheckPoint()
        {
            lock (StatusLock)
            {
                if (status.dwCurrentState != NativeMethods.SERVICE_STOP_PENDING)
                    return;
                status.dwCheckPoint++;
                NativeMethods.SetServiceStatus(statusHandle, ref status);
            }
        }

        // Служба остановлена. serviceExitCode != 0 — служба завершилась с ошибкой (видно в оснастке "Службы").
        private static void ReportStopped(int serviceExitCode)
        {
            lock (StatusLock)
            {
                status.dwWin32ExitCode = serviceExitCode == 0 ? NativeMethods.NO_ERROR : NativeMethods.ERROR_SERVICE_SPECIFIC_ERROR;
                status.dwServiceSpecificExitCode = serviceExitCode;
            }
            SetState(NativeMethods.SERVICE_STOPPED, 0);
        }
    }
}
