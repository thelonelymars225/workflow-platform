using Microsoft.EntityFrameworkCore;

namespace Notification.Worker.Data;

// Configure a database provider and add entity sets when persistence is implemented.
public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
}
