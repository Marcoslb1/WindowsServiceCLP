using ClpCash.DTO;
using ClpCash.Infraestructure;
using ClpCash.Repository;
using Dapper;
using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace WinServiceCLP
{
    [RunInstaller(true)]
    public partial class Service1 : ServiceBase
    {
        int intervalo = Convert.ToInt32(ConfigurationManager.AppSettings["timer"].ToString());

        //public void onDebug()
        //{
        //    OnStart(null);
        //}

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            Thread thread = new Thread(ConsultaBox);
            //ConsultaBox(null);
            thread.Start();
        }

        protected override void OnStop()
        {
        }

        private void ConsultaBox(object sender)
        {
            while (true)
            {
                using (var conn = SingleConnection.GetConnection())
                {
                    try
                    {
                        StringBuilder select = new StringBuilder();

                        select.Append("SELECT" +
                                        " clp.id_clpcash," +
                                        " clp.Des_ipv4," +
                                        " clp.des_cofre," +
                                        " clp.cod_func," +
                                        " clp.cod_regional, " +
                                        " clp.cod_filial," +
                                        " clp.Tp_comandoacao," +
                                        " clp.nom_tecnico," +
                                        " clp.Dt_Inclusao," +
                                        " clp.Dt_Alteracao," +
                                        " clp.Flg_Status," +
                                        " clp.Flg_Situacao" +
                                " FROM CAS_ClpCash clp" +
                                " WHERE Flg_Status = 1");

                        var clpList = conn.Query<CLPCash>(select.ToString());

                        foreach (CLPCash clp in clpList)
                        {
                            clp.Des_senhaclp = BaseRepository.ConsultaSenhaFilial(clp).ToString();
                            MontagemCommand(clp);
                        }
                    }
                    catch (Exception e)
                    {
                        throw new ArgumentException("Erro ao consultar registro enviado pelo CASH no banco!!", e);
                    }
                }
                Thread.Sleep(intervalo);
            }
        }

        public static void MontagemCommand(CLPCash clp)
        {
            var path = ConfigurationManager.AppSettings["path"];

            if (path == "" || path == null)
            {
                throw new ArgumentException("Erro ao encontrar exe do CyproComserver no servidor!!");
            }

            StringBuilder cmdAcao = new StringBuilder();

            cmdAcao.Append(path);
            cmdAcao.Append(@"\CybroComServer.exe");

            cmdAcao.Append(" /b=#IPV4");
            cmdAcao.Replace("#IPV4", clp.Des_ipv4);

            cmdAcao.Append(" /readall=#COFRE");
            cmdAcao.Replace("#COFRE", clp.Des_cofre);

            cmdAcao.Append(" /p=#SENHA");
            cmdAcao.Replace("#SENHA", clp.Des_senhaclp);

            cmdAcao.Append(" #COFRE.");
            cmdAcao.Replace("#COFRE", clp.Des_cofre);

            cmdAcao.Append("#COMANDO");
            cmdAcao.Replace("#COMANDO", clp.Nom_tecnico);

            ExecutarCMD(cmdAcao.ToString(), clp);

        }

        public static void ExecutarCMD(string comando, CLPCash clp)
        {
            using (Process processo = new Process())
            {
                LogUser logUser = new LogUser();

                processo.StartInfo.FileName = Environment.GetEnvironmentVariable("comspec");

                processo.StartInfo.Arguments = string.Format("/c {0}", comando);

                processo.StartInfo.RedirectStandardOutput = true;
                processo.StartInfo.UseShellExecute = false;
                processo.StartInfo.CreateNoWindow = true;

                processo.Start();
                //processo.WaitForExit();

                logUser.Des_logacao = processo.StandardOutput.ReadToEnd();

                string acao = "";
                if (logUser.Des_logacao.Contains("xml"))
                    acao = Util.CapturarStatusAtual(logUser.Des_logacao, clp);

                if (acao.Equals("0") || acao == "0")
                    InserirLog(logUser, clp);
            }
        }

        public static void InserirLog(LogUser logUser, CLPCash clp)
        {
            using (var conn = SingleConnection.GetConnection())
            {
                try
                {
                    StringBuilder sql = new StringBuilder();

                    logUser.Des_logacao = Util.TrataString(logUser.Des_logacao);

                    sql.Append("INSERT INTO " +
                                "CAS_LogBox (" +
                                        "Cod_func, " +
                                        "cod_regional," +
                                        "cod_filial, " +
                                        "Des_LogAcao, " +
                                        "tp_comandoacao," +
                                        "Dt_Inclusao, " +
                                        "Flg_Situacao," +
                                        "ref_clpcash) " +
                                            "\r\nVALUES (" +
                                                    "@Cod_func," +
                                                    "@Cod_regional," +
                                                    "@Cod_filial," +
                                                    "@Des_logacao," +
                                                    "@Tp_comandoAcao," +
                                                    "getdate()," +
                                                    "@Flg_Situacao," +
                                                    "@Id_ClpCash" + ") ");

                    conn.Execute(sql.ToString(), new
                    {
                        clp.Cod_func,
                        clp.Cod_regional,
                        clp.Cod_filial,
                        logUser.Des_logacao,
                        clp.Tp_comandoAcao,
                        clp.Flg_Situacao,
                        clp.Id_ClpCash
                    });

                    SingleConnection.CloseConnection();

                    AlteraFlag(clp);

                }
                catch (Exception e)
                {
                    throw new ArgumentException("Erro ao inserir log de registro na tabela!!", e);
                }
            }
        }

        public static void AlteraFlag(CLPCash clp)
        {
            using (var conn = SingleConnection.GetConnection())
            {
                try
                {
                    StringBuilder update = new StringBuilder();

                    update.Append("update CAS_ClpCash set flg_status = @acao, dt_alteracao = getdate() where id_clpCash = @id_clpCash");
                    update.Replace("@id_clpCash", clp.Id_ClpCash.ToString());
                    update.Replace("@acao", "0");

                    conn.Query<CLPCash>(update.ToString());

                    SingleConnection.CloseConnection();
                }
                catch (Exception e)
                {
                    throw new ArgumentException("Erro ao alterar flag!!", e);
                }
            }
        }

    }
}
