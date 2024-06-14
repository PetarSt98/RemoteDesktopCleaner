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
        private string? report;


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

            this.users = users;
            this.computers = computers;
            this.fileType = fileType;

        }

        public void GenerateTextFile()
        {
            StringWriter stringWriter = new StringWriter();

            if (fileType == FileType.RAP && users != null) {
                stringWriter.WriteLine("------------------------------------------");
                stringWriter.WriteLine($"List of deleted Users-RAPs:");

                foreach (string user in users)
                {
                    stringWriter.WriteLine(user);
                }
                stringWriter.WriteLine("------------------------------------------");
            }

            if (fileType == FileType.RAP_Resource && users != null && computers != null)
            {
                stringWriter.WriteLine("------------------------------------------");
                stringWriter.WriteLine($"List of deleted Configurations:");

                var configurations = users.Zip(computers, (first, second) => new { user = first, computer = second });


                foreach (var configuration in configurations)
                {
                    stringWriter.WriteLine($"Deleted Configuration: User - {configuration.user}  Device - {configuration.computer}");
                }
                stringWriter.WriteLine("------------------------------------------");
            }

            report = stringWriter.ToString();
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

