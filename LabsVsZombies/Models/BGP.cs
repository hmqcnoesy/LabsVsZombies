using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.OracleClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;

namespace LabsVsZombies.Models
{
    public class BGP
    {
        private ServiceController _service;
        private Process _backgroundProcess;
        private Process _nautilusProcess;
        private string _nautilusConnectionString;

        public string ServiceName { get { return this._service.DisplayName; } }
        public string ServiceStatus { get { return this._service.Status.ToString(); } }
        public string BgpId { get; private set; }
        public string TableSuffix { get { return this.BgpId == "1" ? string.Empty : "_" + this.BgpId; } }
        public string BackgroundProcessName { get { return this._backgroundProcess?.ProcessName;  } }
        public bool BackgroundProcessExists { get { return this._backgroundProcess != null; } }
        public string NautilusProcessId { get { return this._nautilusProcess?.Id.ToString(); } }
        public bool NautilusDbSessionExists { get; private set; }

        public bool NautilusProcessResponding { get { return _nautilusProcess == null ? false : _nautilusProcess.Responding; } }

        public bool IsZombie { get { return !NautilusProcessResponding || !NautilusDbSessionExists;  } }
        public string RefreshTime { get; set; }
        public List<BackgroundTask> BackgroundTasks { get; private set; } = new List<BackgroundTask>();
        public List<InstrumentFile> InstrumentFiles { get; private set; } = new List<InstrumentFile>();


        private static IEnumerable<BGP> GetBGPs(string serviceName = null)
        {
            var bgps = new List<BGP>();
            var services = ServiceController.GetServices().Where(s => s.DisplayName.StartsWith("BGP#") || s.DisplayName.StartsWith("DEFBGP#"));
            if (!string.IsNullOrEmpty(serviceName)) services = services.Where(s => s.DisplayName == serviceName);
            foreach (var service in services)
            {
                var bgp = new BGP() { _service = service, RefreshTime = DateTime.Now.ToString("h:mm:ss.f") };
                bgp.BgpId = service.ServiceName.Split('#').Last();
                bgps.Add(bgp);

                var query = $"SELECT ProcessId FROM Win32_Service WHERE Name='{service.ServiceName}'";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    var mgmtObj = searcher.Get().Cast<ManagementObject>().SingleOrDefault();
                    if (mgmtObj == null) continue;
                    var processId = mgmtObj["ProcessId"];
                    if (processId == null || Convert.ToInt32(processId) == 0) continue;
                    var process = Process.GetProcessById(Convert.ToInt32(processId));
                    if (process == null) continue;
                    bgp._backgroundProcess = process;

                    searcher.Query = new ObjectQuery("SELECT * FROM Win32_Process WHERE ParentProcessId=" + process.Id);
                    mgmtObj = searcher.Get().Cast<ManagementObject>().SingleOrDefault();
                    if (mgmtObj == null) continue;

                    bgp._nautilusProcess = Process.GetProcessById(Convert.ToInt32(mgmtObj["ProcessId"]));
                    bgp._nautilusConnectionString = GetConnectionString(bgp._nautilusProcess);
                    bgp.PopulateDbInfo();
                }
            }

            return bgps;
        }


        public static IEnumerable<string> GetLocalhostBgpNames()
        {
            var serviceNames = new List<string>();
            var services = ServiceController.GetServices().Where(s => s.DisplayName.StartsWith("BGP#") || s.DisplayName.StartsWith("DEFBGP#"));
            return services.Select(s => s.DisplayName).OrderByDescending(x => x.Substring(0,1)).ThenBy(x => x).ToList();
        }


        public static IEnumerable<BGP> GetLocalhostBGPs()
        {
            return GetBGPs().OrderByDescending(b => b.ServiceName.Substring(0, 1)).ThenBy(b => b.ServiceName);
        }


        public static BGP GetLocalhostBGP(string serviceName)
        {
            return GetBGPs(serviceName).SingleOrDefault();
        }


        private static string GetConnectionString(Process process)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
            using (var objects = searcher.Get())
            {
                var commandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
                var split = commandLine.Split('/');
                var ocsb = new OracleConnectionStringBuilder();
                ocsb.DataSource = split.SingleOrDefault(x => x.StartsWith("SERVER="))?.Substring(7).Trim();
                ocsb.UserID = split.SingleOrDefault(x => x.StartsWith("USER="))?.Substring(5).Trim();
                ocsb.Password = split.SingleOrDefault(x => x.StartsWith("PASSWORD="))?.Substring(9).Trim();
                return ocsb.ConnectionString;
            }
        }


        // public method for documentation purposes, we show the SQL directly on the page.
        public static string GetDbQuery()
        {
            var isOracleRac = ConfigurationManager.AppSettings["isOracleRac"].ToUpper().Trim() == "YES";
            var view = isOracleRac ? "gv$session" : "v$session";

            return "select count(v.process)\r\n"
                    + "from lims_sys..current_session cs\r\n"
                    + "join lims_sys.lims_session s on cs.session_id = s.session_id\r\n"
                    + $"join {view} v on v.audsid = cs.database_session_id\r\n"
                    + "where s.session_type = 'B'\r\n"
                    + "and v.process like :windows_session_id || ':%'";
        }


        private void PopulateDbInfo()
        {
            using (var connection = new OracleConnection(this._nautilusConnectionString))
            {
                connection.Open();
                var sql = BGP.GetDbQuery();
                var cmd = new OracleCommand(sql, connection);
                cmd.Parameters.AddWithValue(":windows_session_id", this._nautilusProcess.Id);
                this.NautilusDbSessionExists = (decimal)cmd.ExecuteScalar() > 0;

                sql = "select b.background_id, s.os_user_name, b.schedule_id, b.parameter, b.active, btt.name task_type "
                    + $"from lims_sys.background{this.TableSuffix} b "
                    + "left outer join lims_sys.lims_session s on s.session_id = b.session_id "
                    + "left outer join lims_sys.background_task_type btt on b.background_task_type_id = btt.background_task_type_id ";
                cmd = new OracleCommand(sql, connection);
                var reader = cmd.ExecuteReader();
                this.BackgroundTasks = new List<BackgroundTask>();
                while (reader.Read())
                {
                    this.BackgroundTasks.Add(new BackgroundTask
                    {
                        ServiceName = this.ServiceName,
                        BackgroundId = reader.GetDecimal(0),
                        SessionUser = reader.IsDBNull(1) ? string.Empty : reader[1].ToString(),
                        ScheduleId = reader.IsDBNull(2) ? null : (decimal?)reader.GetDecimal(2),
                        Parameter = reader[3].ToString(),
                        Active = reader.IsDBNull(4) ? false : reader[4].ToString() == "T",
                        TaskType = reader[5].ToString()
                    });
                }

                sql = "select i.name, ic.input_file_directory, ic.input_file_extension, nvl(ic.input_file_subdirectories, 'F') "
                    + "from lims_sys.instrument i "
                    + "join lims_sys.instrument_control ic on ic.instrument_control_id = i.instrument_control_id " 
                    + "where i.bgp_id = :bgp_id "
                    + "and i.in_use = 'T'";
                cmd = new OracleCommand(sql, connection);
                cmd.Parameters.AddWithValue(":bgp_id", this.BgpId);
                reader = cmd.ExecuteReader();
                this.InstrumentFiles = new List<InstrumentFile>();
                while (reader.Read())
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(reader[1].ToString(), "*." + reader[2].ToString(), reader[3].ToString() == "T" ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                        foreach (var file in files) 
                        {
                            this.InstrumentFiles.Add(new InstrumentFile
                            {
                                ServiceName = this.ServiceName,
                                InstrumentName = reader[0].ToString(),
                                FileName = file,
                                LastWriteTime = File.GetLastWriteTime(file).ToString("M/d/yy HH:mm")
                            }); ;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.InstrumentFiles.Add(new InstrumentFile { InstrumentName = reader[0].ToString(), FileName = ex.Message });
                    }
                }
            }
        }


        public void RestartBgp()
        {
            if (this._nautilusProcess != null) this._nautilusProcess.Kill();
            if (this._service.Status != ServiceControllerStatus.Stopped)
            {
                this._service.Stop();
                this._service.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 0, 5));
            }
            this._service.Start();
        }
    }
}