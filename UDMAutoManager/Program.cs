using FISCA;
using FISCA.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UDMAutoManager
{
    public class Program
    {
        [FISCA.MainMethod(StartupPriority.LastAsynchronized)]
        public static void main()
        {
            //診斷模式不要執行 UDM 更新。
            if (!RTContext.IsDiagMode)
            {
                Task.Factory.StartNew(() => ProcessUdmUpdates());
            }
        }

        private static void ProcessUdmUpdates()
        {
            DataRowCollection rows;

            try
            {
                QueryHelper q = new QueryHelper();
                rows = q.Select("select url from _udm").Rows;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return;
            }

            Parallel.ForEach(rows.Cast<DataRow>(), row =>
            {
                string url = row["url"] + "";

                try
                {
                    ServerModule.AutoManaged(url);
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            });
        }
    }
}
