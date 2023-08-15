﻿/* DO NOT EDIT THIS FILE */
#nullable disable
namespace Generated;
public class LogEntry
{
    public int logID { get; set; }
    public string logSeverity { get; set; }
    public DateTime? logRecorded { get; set; }
    public string logMessage { get; set; }
    public string logStackTrace { get; set; }
    public class Repository
    {
        //TODO Code to inject database service interface
        public LogEntry LogEntry_Create(string logSeverity,string logMessage,string logStackTrace)
        {
            //TODO Code to read results from proc
            return default;
            //return db.Execute<LogEntry>("LogEntry_Create", new { 
            //    logSeverity,
            //    logMessage,
            //    logStackTrace
            //});
        }
        public List<LogEntry> LogEntry_GetRange(DateTime? startDate,DateTime? endDate)
        {
            //TODO Code to read results from proc
            return default;
            //return db.Execute<List<LogEntry>>("LogEntry_GetRange", new { 
            //    startDate,
            //    endDate
            //});
        }
        public void LogEntry_CleanUp()
        {
            //TODO Code to execute void-result proc
            //db.Execute("LogEntry_CleanUp", new { 

            //});
        }
    }
    public interface IService
    {
        LogEntry Create(string logSeverity,string logMessage,string logStackTrace);
        int Count(DateTime? startDate);
        Dictionary<string, int> CountSeverities(DateTime? startDate);
        void CleanUp();
    }
    public interface IBackendService : IService
    {
        // Implement and inject this interface as a separate service
    }
    public class DbService : IService
    {
        //TODO Inject database wrapper service
        private readonly IBackendService impl;
        public DbService(IBackendService impl)
        {
            this.impl = impl;
        }
        public LogEntry Create(string logSeverity,string logMessage,string logStackTrace)
        {
            //TODO Code to execute via DB wrapper
            return /*wrapper.Execute<LogEntry>(() => */impl.Create(
                  logSeverity,
                  logMessage,
                  logStackTrace
                  )/*)*/;
        }
        public int Count(DateTime? startDate)
        {
            //TODO Code to execute via DB wrapper
            return /*wrapper.Execute<int>(() => */impl.Count(
                  startDate
                  )/*)*/;
        }
        public Dictionary<string, int> CountSeverities(DateTime? startDate)
        {
            //TODO Code to execute via DB wrapper
            return /*wrapper.Execute<Dictionary<string, int>>(() => */impl.CountSeverities(
                  startDate
                  )/*)*/;
        }
        public void CleanUp()
        {
            //TODO Code to execute via DB wrapper
            /*wrapper.Execute(() => */impl.CleanUp(

                  )/*)*/;
        }
    }
    public class ApiService : IService
    {
        //TODO Inject HTTP client service
        public ApiService()
        {
        }
        public LogEntry Create(string logSeverity,string logMessage,string logStackTrace)
        {
            //TODO Code to execute via API client
            return default;
            //return api.Execute<LogEntry>("LogEntry/Create", new {
            //    logSeverity,
            //    logMessage,
            //    logStackTrace
            //});
        }
        public int Count(DateTime? startDate)
        {
            //TODO Code to execute via API client
            return default;
            //return api.Execute<int>("LogEntry/Count", new {
            //    startDate
            //});
        }
        public Dictionary<string, int> CountSeverities(DateTime? startDate)
        {
            //TODO Code to execute via API client
            return default;
            //return api.Execute<Dictionary<string, int>>("LogEntry/CountSeverities", new {
            //    startDate
            //});
        }
        public void CleanUp()
        {
            //TODO Code to execute via API client
            //api.Execute("LogEntry/CleanUp", new {

            //});
        }
    }
}