using MySql.Data.EntityFramework;
using System.Data.Entity;


namespace RemoteDesktopCleaner.Data
{
    [DbConfigurationType(typeof(MySqlEFConfiguration))]
    public partial class RapContext : DbContext
    {
        public RapContext()
            : base("name=MySQL_DB")
        {
        }

        public virtual DbSet<rap> raps { get; set; }
        public virtual DbSet<rap_resource> rap_resource { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<rap>()
                .Property(e => e.name)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .Property(e => e.description)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .Property(e => e.login)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .Property(e => e.port)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .Property(e => e.resourceGroupName)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .Property(e => e.resourceGroupDescription)
                .IsUnicode(false);

            modelBuilder.Entity<rap>()
                .HasMany(e => e.rap_resource)
                .WithRequired(e => e.rap)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<rap_resource>()
                .Property(e => e.RAPName)
                .IsUnicode(false);

            modelBuilder.Entity<rap_resource>()
                .Property(e => e.resourceName)
                .IsUnicode(false);

            modelBuilder.Entity<rap_resource>()
                .Property(e => e.resourceOwner)
                .IsUnicode(false);
        }
    }
}
