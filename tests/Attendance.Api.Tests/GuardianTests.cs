using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Endpoints;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Tests;

public class GuardianTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    async Task<PeopleEndpoints.PersonDetail> DetailAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<PeopleEndpoints.PersonDetail>($"/api/people/{id}", Json, Ct))!;

    [Fact]
    public async Task Newcomer_with_a_guardian_name_keeps_the_phone_on_the_guardian()
    {
        var group = await app.CreateGroupAsync();
        var leader = await app.ClientForAsync(await app.CreateUserAsync(Roles.Leader, [group.Id]));

        var res = await leader.PostAsJsonAsync($"/api/groups/{group.Id}/newcomers?date={Sunday:yyyy-MM-dd}",
            new { name = "新朋友甲", guardianName = " 甲媽媽 ", guardianRelation = "Mother", guardianPhone = "0911000111", consent = true },
            Json, Ct);

        res.EnsureSuccessStatusCode();
        var id = Assert.Single((await res.Content.ReadFromJsonAsync<RollcallDto>(Json, Ct))!.People).PersonId;
        var admin = await app.ClientForAsync(Roles.Admin);
        var person = await DetailAsync(admin, id);
        Assert.Null(person.Phone);
        var guardian = Assert.Single(person.Guardians);
        Assert.Equal(("甲媽媽", "0911000111", RelationType.Mother, true),
            (guardian.Name, guardian.Phone, guardian.RelationType, guardian.IsPrimaryContact));
    }

    [Fact]
    public async Task Staff_add_a_member_with_a_guardian_and_the_list_shows_and_searches_it()
    {
        var group = await app.CreateGroupAsync();
        var client = await app.ClientForAsync(await app.CreateUserAsync(Roles.Coordinator, ministryIds: [group.MinistryId]));

        var res = await client.PostAsJsonAsync("/api/people", new
        {
            name = "有家長的孩子", gender = "Male", phone = "0922000222", groupId = group.Id, consent = true,
            guardianName = "王家長搜尋", guardianRelation = "Father",
        }, Json, Ct);
        res.EnsureSuccessStatusCode();

        var found = await client.GetFromJsonAsync<List<PeopleEndpoints.PersonListItem>>(
            "/api/people?search=王家長搜尋", Json, Ct);
        var item = Assert.Single(found!);
        Assert.Equal("有家長的孩子", item.Name);
        Assert.Equal(["王家長搜尋"], item.Guardians);
        var guardian = Assert.Single((await DetailAsync(client, item.Id)).Guardians);
        Assert.Equal("0922000222", guardian.Phone);
    }

    [Fact]
    public async Task Guardians_can_be_added_edited_and_removed()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "編輯家長");
        var client = await app.ClientForAsync(Roles.Admin);

        var first = await client.PostAsJsonAsync($"/api/people/{id}/guardians",
            new { name = "爸爸", phone = "0933", relationType = "Father", isPrimaryContact = false }, Json, Ct);
        var second = await client.PostAsJsonAsync($"/api/people/{id}/guardians",
            new { name = "媽媽", relationType = "Mother", isPrimaryContact = false }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var guardians = (await DetailAsync(client, id)).Guardians;
        // The first one became the main contact even though it was not asked for.
        Assert.Equal(["爸爸", "媽媽"], guardians.Select(g => g.Name));
        Assert.True(guardians[0].IsPrimaryContact);
        var mom = guardians[1].PersonId;

        (await client.PutAsJsonAsync($"/api/people/{id}/guardians/{mom}",
            new { name = "媽媽改名", phone = "0944", relationType = "Mother", isPrimaryContact = true }, Json, Ct))
            .EnsureSuccessStatusCode();
        guardians = (await DetailAsync(client, id)).Guardians;
        Assert.Equal(("媽媽改名", "0944"), (guardians[0].Name, guardians[0].Phone));
        Assert.Single(guardians, g => g.IsPrimaryContact);

        (await client.DeleteAsync($"/api/people/{id}/guardians/{mom}", Ct)).EnsureSuccessStatusCode();
        var left = Assert.Single((await DetailAsync(client, id)).Guardians);
        Assert.Equal("爸爸", left.Name);
        Assert.True(left.IsPrimaryContact);
        // Nothing else refers to the removed guardian, so their record is gone too.
        Assert.False(await app.WithDbAsync(db => db.People.AnyAsync(p => p.Id == mom)));
    }

    [Fact]
    public async Task Removing_a_guardian_keeps_them_when_another_child_still_has_them()
    {
        var group = await app.CreateGroupAsync();
        var (a, b, parent) = await app.WithDbAsync(async db =>
        {
            var shared = new Person { Name = "共同家長" };
            var kids = new[] { new Person { Name = "哥哥" }, new Person { Name = "妹妹" } };
            foreach (var kid in kids)
            {
                kid.Guardians.Add(new PersonRelationship { RelatedPerson = shared, RelationType = RelationType.Mother, IsPrimaryContact = true });
                db.GroupMemberships.Add(new GroupMembership { GroupId = group.Id, Person = kid, StartDate = new DateOnly(2020, 1, 1) });
            }
            await db.SaveChangesAsync();
            return (kids[0].Id, kids[1].Id, shared.Id);
        });
        var client = await app.ClientForAsync(Roles.Admin);

        (await client.DeleteAsync($"/api/people/{a}/guardians/{parent}", Ct)).EnsureSuccessStatusCode();

        Assert.Empty((await DetailAsync(client, a)).Guardians);
        Assert.Equal("共同家長", Assert.Single((await DetailAsync(client, b)).Guardians).Name);
    }

    [Fact]
    public async Task Guardian_needs_a_name_and_belongs_to_the_child()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "驗證家長");
        var other = await app.AddMemberAsync(group.Id, "別人");
        var client = await app.ClientForAsync(Roles.Admin);

        var noName = await client.PostAsJsonAsync($"/api/people/{id}/guardians",
            new { name = " ", relationType = "Father", isPrimaryContact = true }, Json, Ct);
        var notTheirs = await client.PutAsJsonAsync($"/api/people/{id}/guardians/{other}",
            new { name = "x", relationType = "Father", isPrimaryContact = true }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
    }

    [Fact]
    public async Task Teachers_cannot_edit_guardians()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "老師不能改");
        var leader = await app.ClientForAsync(await app.CreateUserAsync(Roles.Leader, [group.Id]));

        var res = await leader.PostAsJsonAsync($"/api/people/{id}/guardians",
            new { name = "家長", relationType = "Father", isPrimaryContact = true }, Json, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
