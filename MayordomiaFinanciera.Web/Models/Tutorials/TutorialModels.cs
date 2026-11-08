using System;

namespace MayordomiaFinanciera.Web.Models.Tutorials
{
    public class PlatformTutorialDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? YoutubeUrl { get; set; }
        public string? StepByStepContent { get; set; }
        public string IconClass { get; set; } = "fa-solid fa-book-open";
        public int DisplayOrder { get; set; }
                public bool IsPublished { get; set; } = true;
    }
}