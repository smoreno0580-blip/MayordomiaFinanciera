using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Core.Interfaces
{
    public interface IEmailService
    {
        Task SendRegistrationConfirmationAsync(string toEmail, string fullName, string groupName, string username, string rawPassword);
    }
}