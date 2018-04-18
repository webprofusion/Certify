﻿using Certify.Models.Providers;
using Serilog;
using System;
using System.Collections.Generic;

namespace Certify.Models
{
    public enum LogItemType
    {
        GeneralInfo = 1,
        GeneralWarning = 10,
        GeneralError = 20,
        CertificateRequestStarted = 50,
        CertificateRequestSuccessful = 100,
        CertficateRequestFailed = 101,
        CertficateRequestAttentionRequired = 110
    }

    public class Loggy : Providers.ILog
    {
        private ILogger _log;

        public Loggy(ILogger log)
        {
            _log = log;
        }

        public void Error(string template, params object[] propertyValues)
        {
            _log.Error(template, propertyValues);
        }

        public void Error(Exception exp, string template, params object[] propertyValues)
        {
            _log.Error(exp, template, propertyValues);
        }

        public void Information(string template, params object[] propertyValues)
        {
            _log.Information(template, propertyValues);
        }

        public void Verbose(string template, params object[] propertyValues)
        {
            _log.Verbose(template, propertyValues);
        }

        public void Warning(string template, params object[] propertyValues)
        {
            _log.Warning(template, propertyValues);
        }
    }

    public class ManagedCertificateLogItem
    {
        public DateTime EventDate { get; set; }
        public string Message { get; set; }
        public LogItemType LogItemType { get; set; }
    }

    public class Util
    {
        public const string APPDATASUBFOLDER = "Certify";

        public static string GetAppDataFolder()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\" + APPDATASUBFOLDER;
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
            return path;
        }
    }

    public static class ManagedCertificateLog
    {
        private static Dictionary<string, Serilog.Core.Logger> _managedItemLoggers { get; set; }

        public static string GetLogPath(string managedItemId)
        {
            return Util.GetAppDataFolder() + "\\logs\\log_" + managedItemId.Replace(':', '_') + ".txt";
        }

        public static ILog GetLogger(string managedItemId)
        {
            if (_managedItemLoggers == null) _managedItemLoggers = new Dictionary<string, Serilog.Core.Logger>();

            Serilog.Core.Logger log = null;

            if (_managedItemLoggers.ContainsKey(managedItemId))
            {
                log = _managedItemLoggers[managedItemId];
            }
            else
            {
                var logPath = GetLogPath(managedItemId);

                try
                {
                    if (System.IO.File.Exists(logPath) && new System.IO.FileInfo(logPath).Length > (1024 * 1024))
                    {
                        System.IO.File.Delete(logPath);
                    }
                }
                catch { }

                log = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Debug()
                    .WriteTo.File(logPath, shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                    .CreateLogger();

                _managedItemLoggers.Add(managedItemId, log);
            }
            return new Loggy(log);
        }

        public static void AppendLog(string managedItemId, ManagedCertificateLogItem logItem)
        {
            var log = GetLogger(managedItemId);

            if (logItem.LogItemType == LogItemType.CertficateRequestFailed)
            {
                log.Error(logItem.Message);
            }
            else if (logItem.LogItemType == LogItemType.GeneralError)
            {
                log.Error(logItem.Message);
            }
            if (logItem.LogItemType == LogItemType.GeneralWarning)
            {
                log.Warning(logItem.Message);
            }
            else
            {
                log.Information(logItem.Message);
            }
        }

        public static void DisposeLoggers()
        {
            if (_managedItemLoggers.Count > 0)
            {
                foreach (var l in _managedItemLoggers.Values)
                {
                    l.Dispose();
                }
            }
        }
    }
}