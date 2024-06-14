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

        private List<string>? users { get; set; }
        private List<string>? computers { get; set; }
        private FileType fileType { get; set; }
        private string? report;

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

