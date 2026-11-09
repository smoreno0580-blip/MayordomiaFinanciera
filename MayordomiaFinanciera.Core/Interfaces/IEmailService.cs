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

        Task SendEmailAsync(string toEmail, string subject, string body);

        // Nuevo método para notificar boletines / avisos eclesiales
        Task SendBulletinNotificationAsync(string email, string memberName, string churchName, string bulletinTitle, string bulletinContent);
    }
}