using Microsoft.AspNetCore.Identity.UI.Services;

namespace AssetManagement.Services
{
    public class EmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // No-op (for development)
            return Task.CompletedTask;
        }

    }
}
