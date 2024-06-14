using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace RemoteDesktopCleaner.BackgroundServices.ConfigurationValidation
{
    public class CleaningEmailingService
    {
        public enum FileType
        {
            RAP,
            RAP_Resource
        }

        public List<string>? users { get; set; }
        public List<string>? computers { get; set; }
        public FileType fileType { get; set; }
        public string? report;


        public CleaningEmailingService(List<string> users, FileType fileType)
        {
            if (this.users != null && this.users.Count > 0)
            {
                this.users.Clear();
                this.users = null;
            }

            this.users = users;
            this.fileType = fileType;

        }

        public CleaningEmailingService(List<string> users, List<string> computers, FileType fileType)
        {
            if (this.users != null && this.users.Count > 0)
            {
                this.users.Clear();
                this.users = null;
            }

            if (this.computers != null && this.computers.Count > 0)
            {
                this.computers.Clear();
                this.computers = null;
            }

            if (computers.Count != users.Count)
            {
                throw new Exception("Invalid configuration order (number of devices and users should be equal) in cleaning email service!");
            }

            this.users = users;
            this.computers = computers;
            this.fileType = fileType;
        }

        public string GenerateTextFile()
        {
            StringWriter stringWriter = new StringWriter();

            if (fileType == FileType.RAP && users != null) {
                stringWriter.WriteLine("==========================================");
                stringWriter.WriteLine($"List of deleted RAPs:");
                stringWriter.WriteLine("------------------------------------------");

                foreach (string user in users)
                {
                    stringWriter.WriteLine($"User: {user}\n");
                }
            }

            if (fileType == FileType.RAP_Resource && users != null && computers != null)
            {
                stringWriter.WriteLine("==========================================");
                stringWriter.WriteLine($"List of deleted Configurations:");
                stringWriter.WriteLine("------------------------------------------");

                var configurations = users.Zip(computers, (first, second) => new { user = first, computer = second });


                foreach (var configuration in configurations)
                {
                    stringWriter.WriteLine($"User: {configuration.user}  Device: {configuration.computer}\n");
                }
            }

            if (report == null || report.Length == 0)
            {
                report = stringWriter.ToString();
            }
            else
            {
                report += '\n';
                report += stringWriter.ToString();
            }

            return report;
        }

        public void ConcatenateReports(string reportRap, string reportRapResource)
        {
            report = reportRap + '\n' + reportRapResource;

        }

        public void SendEmail()
        {
            try
            {
                MailMessage message = new MailMessage();

                message.From = new MailAddress("noreply@cern.ch");
                message.To.Add(new MailAddress(ConfigurationManager.AppSettings["admins_email"]));
                message.Subject = "Remote Desktop Gateway Cleaning Report";
                message.Body = report;
                message.IsBodyHtml = false;

                SmtpClient client = new SmtpClient("cernmx.cern.ch");
                client.Send(message);

                Console.WriteLine($"Send an email to {ConfigurationManager.AppSettings["admins_email"]}");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

}

