using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using MayordomiaFinanciera.Core.Interfaces;
using MayordomiaFinanciera.Core.Settings;

namespace MayordomiaFinanciera.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly SmtpSettings _smtpSettings;

        public EmailService(IOptions<SmtpSettings> smtpSettings)
        {
            _smtpSettings = smtpSettings.Value;
        }

        public async Task SendRegistrationConfirmationAsync(string toEmail, string fullName, string groupName, string username, string rawPassword)
        {
            var subject = "¡Bienvenido a Mayordomía Financiera! Confirmación de Registro";

            var body = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; background-color: #f4f6f9; color: #333; margin: 0; padding: 20px; }}
                    .container {{ max-width: 600px; background: #ffffff; margin: 0 auto; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.05); }}
                    .header {{ background-color: #1a365d; color: #ffffff; padding: 25px; text-align: center; }}
                    .header h1 {{ margin: 0; font-size: 22px; }}
                    .content {{ padding: 30px; line-height: 1.6; }}
                    .credentials-box {{ background-color: #f8fafc; border-left: 4px solid #d97706; padding: 15px; margin: 20px 0; border-radius: 4px; }}
                    .credentials-box p {{ margin: 5px 0; font-size: 14px; }}
                    .footer {{ background-color: #edf2f7; text-align: center; padding: 15px; font-size: 12px; color: #718096; }}
                    .btn {{ display: inline-block; background-color: #d97706; color: #ffffff; padding: 10px 20px; text-decoration: none; border-radius: 5px; font-weight: bold; margin-top: 15px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>Finanzas a la Manera de Dios</h1>
                    </div>
                    <div class='content'>
                        <h2>¡Hola, {fullName}!</h2>
                        <p>Tu registro en la plataforma de <strong>Mayordomía Financiera</strong> ha sido completado con éxito.</p>
                        
                        <div class='credentials-box'>
                            <p><strong>Grupo asignado:</strong> {groupName}</p>
                            <p><strong>Usuario / Correo:</strong> {username}</p>
                            <p><strong>Contraseña:</strong> {rawPassword}</p>
                        </div>

                        <p>Para explorar más recursos, eventos y materiales informativos, te invitamos a visitar nuestro sitio oficial:</p>
                        <p style='text-align: center;'>
                            <a href='https://mayordomiayfinanzas.com' class='btn' target='_blank'>Visitar Mayordomía y Finanzas</a>
                        </p>
                    </div>
                    <div class='footer'>
                        <p>© {DateTime.Now.Year} Mayordomía Financiera Panamá. Todos los derechos reservados.</p>
                        <p><a href='https://mayordomiayfinanzas.com'>mayordomiayfinanzas.com</a></p>
                    </div>
                </div>
            </body>
            </html>";

            using var client = new SmtpClient(_smtpSettings.Server, _smtpSettings.Port)
            {
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpSettings.SenderEmail, _smtpSettings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
        }
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var message = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(_smtpSettings.SenderEmail, _smtpSettings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            using (var client = new System.Net.Mail.SmtpClient(_smtpSettings.Server, _smtpSettings.Port))
            {
                client.Credentials = new System.Net.NetworkCredential(_smtpSettings.SenderEmail, _smtpSettings.Password);
                client.EnableSsl = true;
                await client.SendMailAsync(message);
            }
        }
    }
}