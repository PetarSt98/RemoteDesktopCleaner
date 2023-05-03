using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;


namespace RemoteDesktopCleaner.Data
{
    [Table("RAP_Resource")]
    public partial class rap_resource
    {
        [Key]
        [Column(Order = 0)]
        [StringLength(100)]
        [ForeignKey("rap")]
        public string RAPName { get; set; }

        [Key]
        [Column(Order = 1)]
        [StringLength(100)]
        public string resourceName { get; set; }

        [Required]
        [StringLength(100)]
        public string resourceOwner { get; set; }

        public bool access { get; set; }

        public bool synchronized { get; set; }

        public bool? invalid { get; set; }

        public bool? exception { get; set; }

        [Column(TypeName = "timestamp")]
        public DateTime? createDate { get; set; }

        [Column(TypeName = "timestamp")]
        public DateTime? updateDate { get; set; }

        public bool toDelete { get; set; }

        public virtual rap rap { get; set; }
        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
