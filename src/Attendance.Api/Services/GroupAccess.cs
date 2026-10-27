using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

/// <summary>
/// Which groups the current user may see:
/// Admin → all; Coordinator → groups of their ministries; Leader → the groups they lead.
/// </summary>
public class GroupAccess(AppDbContext db, IHttpContextAccessor http)
{
    public IQueryable<Group> Groups()
    {
        var user = http.HttpContext!.User;
        if (user.IsInRole(Roles.Admin)) return db.Groups;

        var userId = user.GetUserId();
        var isCoordinator = user.IsInRole(Roles.Coordinator);
        return db.Groups.Where(g =>
            db.GroupLeaders.Any(l => l.UserId == userId && l.GroupId == g.Id)
            || (isCoordinator && db.MinistryCoordinators.Any(m => m.UserId == userId && m.MinistryId == g.MinistryId)));
    }

    public IQueryable<int> GroupIds() => Groups().Select(g => g.Id);

    public Task<Group?> FindGroupAsync(int groupId) => Groups().FirstOrDefaultAsync(g => g.Id == groupId);
}
