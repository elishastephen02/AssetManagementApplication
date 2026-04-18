using AssetManagement.Data;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IEmailSender, EmailSender>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSession();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// Seed roles and primary user
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // 1Seed Roles
    string[] roles = { "PrimaryUser", "NormalUser" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Primary User Info
    string primaryUserEmail = "kiyashcsha@gmail.com";
    string primaryUserPassword = "KickAss8*";

    // Check if user exists
    var user = await userManager.FindByEmailAsync(primaryUserEmail);
    if (user == null)
    {
        user = new ApplicationUser
        {
            UserName = primaryUserEmail,
            Email = primaryUserEmail,
            IsApproved = true // Auto approve admin
        };

        var result = await userManager.CreateAsync(user, primaryUserPassword);
        if (!result.Succeeded)
        {
            throw new Exception("Failed to create primary user: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
    else
    {
        // Ensure existing admin is approved
        if (!user.IsApproved)
        {
            user.IsApproved = true;
            await userManager.UpdateAsync(user);
        }
    }

    // Add role
    if (!await userManager.IsInRoleAsync(user, "PrimaryUser"))
    {
        await userManager.AddToRoleAsync(user, "PrimaryUser");
    }
}

app.Run();