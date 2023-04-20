using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class Constants
    {
        public static string OrphanedSid = "S-1-5-";
    }
    public enum LocalGroupFlag
    {
        Add,
        Delete,
        Update,
    }
    public class LocalGroup
    {
        public string Name { get; set; }
        public LocalGroupFlag Flag { get; set; }
        public List<string> Computers { get; } = new List<string>();
        public List<string> Members { get; } = new List<string>();
        public List<string> Content => Computers.Concat(Members).ToList();

        private string _remark;
        public string Remark
        {
            get => string.IsNullOrEmpty(_remark) ? CreateRemark() : _remark;
            set => _remark = value;
        }

        public bool AllGood => Computers.Count > 0 && Members.Count > 0 && Members.TrueForAll(x => !x.StartsWith(Constants.OrphanedSid));

        private string CreateRemark()
        {
            var sb = new StringBuilder();
            if (Name.StartsWith("-"))
                sb.Append("OBSOLETE GROUP.");
            if (Name.StartsWith("+"))
                sb.Append("Group missing in gateway config.");
            if (Computers.Count == 0)
                sb.Append("No computers.");
            if (Members.Count == 0)
                sb.Append("No members.");
            if (Members.Any(member => member.StartsWith(Constants.OrphanedSid)))
                sb.Append("Dead members objects. ");
            if (Computers.Any(computer => computer.StartsWith(Constants.OrphanedSid)))
                sb.Append("Dead computer objects. ");
            return sb.ToString();
        }

        public LocalGroup() { }

        public LocalGroup(string name)
        {
            Name = name;
        }
        public LocalGroup(string name, LocalGroupFlag flag)
        {
            Name = name;
            Flag = flag;
        }
        public LocalGroup(string name, IEnumerable<string> groupContent)
        {
            Name = name;
            var enumerable = groupContent.ToList();
            Computers.AddRange(enumerable.Where(el => el.EndsWith("$")));
            Members.AddRange(enumerable.Where(el => !el.EndsWith("$")));
        }

        public void AddMember(string member)
        {
            Members.Add(member);
        }

        public void AddComputer(string computerName)
        {
            Computers.Add(computerName);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
