namespace MayordomiaFinanciera.Web.Models.Church
{
    public class BulletinDto
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime PublishDate { get; set; } = DateTime.Today;
        public bool IsActive { get; set; } = true;
    }
}