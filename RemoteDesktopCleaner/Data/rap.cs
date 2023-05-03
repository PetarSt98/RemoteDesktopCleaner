using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;


namespace RemoteDesktopCleaner.Data
{
    [Table("RAP")]
    public partial class rap
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public rap()
        {
            rap_resource = new HashSet<rap_resource>();
        }

        [Key]
        [StringLength(100)]
        public string name { get; set; }

        [Column(TypeName = "char")]
        [StringLength(10)]
        public string description { get; set; }

        [Required]
        [StringLength(100)]
        public string login { get; set; }

        [Column(TypeName = "char")]
        [Required]
        [StringLength(10)]
        public string port { get; set; }

        public bool enabled { get; set; }

        [Required]
        [StringLength(100)]
        public string resourceGroupName { get; set; }

        [StringLength(100)]
        public string resourceGroupDescription { get; set; }

        public bool synchronized { get; set; }

        [Column(TypeName = "timestamp")]
        public DateTime lastModified { get; set; }

        public bool toDelete { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<rap_resource> rap_resource { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
