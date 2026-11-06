using System;
using System.Collections.Generic;

namespace MayordomiaFinanciera.Web.Models.Church
{
    public class VolunteerTeamDto
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? LeaderName { get; set; }
        public int MemberCount { get; set; }
        public List<VolunteerMemberDto> Members { get; set; } = new();
    }

    public class VolunteerMemberDto
    {
        public Guid Id { get; set; }
        public Guid TeamId { get; set; }
        public Guid? UserId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string? MemberEmail { get; set; }
        public string? RoleInTeam { get; set; }
    }
}