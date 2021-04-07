using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OracleClient;
using System.Linq;
using System.Web;

namespace LabsVsZombies.Models
{
    public class NautilusDb
    {
        private string _connectionString;
        public NautilusDb(string connectionString)
        {
            _connectionString = connectionString;
        }


        public IEnumerable<int> GetBgpSessionsWindowsProcessIds()
        {
            var processIds = new List<int>();

            using (var connection = new OracleConnection(_connectionString))
            {
                connection.Open();
                var sql = "select v.process "
                    + "from lims.current_session cs "
                    + "join lims_session s on cs.session_id = s.session_id "
                    + "join v$session v on v.audsid = cs.database_session_id "
                    + "where s.session_type = 'B'";
                var cmd = new OracleCommand(sql, connection);
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var process = reader[0].ToString();
                    if (process.Contains(":")) processIds.Add(int.Parse(process.Substring(0, process.IndexOf(":"))));
                }
            }

            return processIds;
        }
    }
}