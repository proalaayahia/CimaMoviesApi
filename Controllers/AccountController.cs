﻿using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using CimaMoviesApi.Data;
using CimaMoviesApi.Dtos.auth;
using CimaMoviesApi.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace AngularToAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AccountController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _manager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AccountController(ApplicationDbContext db, UserManager<ApplicationUser> manage,
            SignInManager<ApplicationUser> signInManager, RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _manager = manage;
            _signInManager = signInManager;
            _roleManager = roleManager;
        }

        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register(RegisterModel model)
        {
            if (model == null)
            {
                return NotFound();
            }
            if (ModelState.IsValid)
            {
                if (EmailExistes(model.Email))
                {
                    return BadRequest("Email is used");
                }
                if (!IsEmailValid(model.Email))
                {
                    return BadRequest("Email not valid!!");
                }
                if (UserNameExistes(model.UserName))
                {
                    return BadRequest("UserName is used");
                }

                var user = new ApplicationUser
                {
                    Email = model.Email,
                    UserName = model.UserName
                };

                var result = await _manager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    ////http://localhost:58314/Account/RegistrationConfirm?ID=545435&Token=5435354gw34523
                    var token = await _manager.GenerateEmailConfirmationTokenAsync(user);
                    var encodeToken = Encoding.UTF8.GetBytes(token);
                    var newToken = WebEncoders.Base64UrlEncode(encodeToken);

                    var confirmLink = $"http://localhost:4200/registerconfirm?ID={user.Id}&Token={newToken}";
                    var txt = "Please confirm your registration at our sute";
                    var link = "<a href=\"" + confirmLink + "\">Confirm registration</a>";
                    var title = "Registration Confirm";
                    if (await SendGridAPI.Execute(user.Email, user.UserName, txt, link, title))
                    {
                        return StatusCode(StatusCodes.Status200OK);
                    }
                }
                else
                {
                    return BadRequest(result.Errors);
                }
            }
            return StatusCode(StatusCodes.Status400BadRequest);
        }

        private bool UserNameExistes(string userName)
        {
            return _db.Users.Any(x => x.UserName == userName);
        }

        private bool EmailExistes(string email)
        {
            return _db.Users.Any(x => x.Email == email);
        }

        private bool IsEmailValid(string email)
        {
            Regex em = new Regex(@"\w+\@\w+.com|\w+\@\w+.net");
            if (em.IsMatch(email))
            {
                return true;
            }
            return false;
        }

        [HttpGet]
        [Route("RegistrationConfirm")]
        public async Task<IActionResult> RegistrationConfirm(string ID, string Token)
        {
            if (string.IsNullOrEmpty(ID) || string.IsNullOrEmpty(Token))
                return NotFound();
            var user = await _manager.FindByIdAsync(ID);
            if (user == null)
                return NotFound();

            var newToken = WebEncoders.Base64UrlDecode(Token);
            var encodeToken = Encoding.UTF8.GetString(newToken);

            var result = await _manager.ConfirmEmailAsync(user, encodeToken);
            if (result.Succeeded)
            {
                return Ok();
            }
            else
            {
                return BadRequest(result.Errors);
            }
        }

        [HttpPost]
        [Route("Login")]
        public async Task<IActionResult> Login(LoginModel model)
        {
            await CreateRoles();
            await CreateAdmin();
            if (model == null)
                return NotFound();
            var user = await _manager.FindByEmailAsync(model.Email);
            if (user == null)
                return NotFound();
            if (!user.EmailConfirmed)
                return Unauthorized("Your Email Has Not Confirmed Yet!");
            var userName = HttpContext.User.Identity?.Name;
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (id != null || userName != null)
            {
                return BadRequest($"user id:{id} is exists");
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, true);
            if (result.Succeeded)
            {
                if (await _roleManager.RoleExistsAsync("User"))
                {
                    if (!await _manager.IsInRoleAsync(user, "User") && !await _manager.IsInRoleAsync(user, "Admin"))
                    {
                        await _manager.AddToRoleAsync(user, "User");
                    }
                }

                var roleName = await GetRoleNameByUserId(user.Id);
                if (roleName != null)
                {
                    HttpContext.Response.Cookies.Append(
                     "name", "value",
                     new CookieOptions() { SameSite = SameSiteMode.Lax });
                    AddCookies(user.UserName!, roleName, user.Id, model.RememberMe, user.Email!);
                }
                return Ok();
            }
            else if (result.IsLockedOut)
            {
                return Unauthorized("Your Account temporarily suspended");
            }
            return StatusCode(StatusCodes.Status204NoContent);
        }

        private async Task<string> GetRoleNameByUserId(string userId)
        {
            var userRole = await _db.UserRoles.FirstOrDefaultAsync(x => x.UserId == userId);
            if (userRole is null)
                return string.Empty;
            var role = await _db.Roles.Where(x => x.Id == userRole.RoleId).Select(x => x.Name).FirstOrDefaultAsync();
            if (role is not null)
                return role;
            return string.Empty;
        }

        private async Task CreateAdmin()
        {
            var admin = await _manager.FindByNameAsync("Admin");
            if (admin == null)
            {
                var user = new ApplicationUser
                {
                    Email = "admin@admin.com",
                    UserName = "Admin",
                    PhoneNumber = "90977643231",
                    EmailConfirmed = true
                };

                var x = await _manager.CreateAsync(user, "123#Aa");
                if (x.Succeeded)
                {
                    if (await _roleManager.RoleExistsAsync("Admin"))
                        await _manager.AddToRoleAsync(user, "Admin");
                }
            }
        }

        private async Task CreateRoles()
        {
            if (_roleManager.Roles.Count() < 1)
            {
                var role = new IdentityRole
                {
                    Name = "Admin"
                };
                await _roleManager.CreateAsync(role);

                role = new IdentityRole
                {
                    Name = "User"
                };
                await _roleManager.CreateAsync(role);
            }
        }

        private async void AddCookies(string username, string roleName, string userId, bool remember, string email)
        {
            var claim = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, roleName),
            };

            var claimIdentity = new ClaimsIdentity(claim, CookieAuthenticationDefaults.AuthenticationScheme);

            if (remember)
            {
                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = true,
                    ExpiresUtc = DateTime.UtcNow.AddDays(10)
                };

                await HttpContext.SignInAsync
                (
                   CookieAuthenticationDefaults.AuthenticationScheme,
                   new ClaimsPrincipal(claimIdentity),
                   authProperties
                );
            }
            else
            {
                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = false,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
                };

                await HttpContext.SignInAsync
                (
                   CookieAuthenticationDefaults.AuthenticationScheme,
                   new ClaimsPrincipal(claimIdentity),
                   authProperties
                );
            }
        }

        [HttpGet]
        [Route("Logout")]
        public async Task<IActionResult> Logout()
        {
            var authProperties = new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = true,
                ExpiresUtc = DateTime.UtcNow.AddDays(10)
            };
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme, authProperties);
            return Ok();
        }

        [HttpGet]
        [Route("GetRoleName/{email}")]
        public async Task<string> GetRoleName(string email)
        {
            var user = await _manager.FindByEmailAsync(email);
            if (user is null)
                return string.Empty;
            var userRole = await _db.UserRoles.FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (userRole is null)
                return string.Empty;
            var roleName = await _db.Roles.Where(x => x.Id == userRole.RoleId).Select(x => x.Name).FirstOrDefaultAsync();
            return roleName ?? string.Empty;
        }

        [Authorize]
        [HttpGet]
        [Route("CheckUserClaims/{email}&{role}")]
        public IActionResult CheckUserClaims(string email, string role)
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userEmail != null && userRole != null && id != null)
            {
                if (email == userEmail && role == userRole)
                {
                    return Ok();
                }
            }
            return StatusCode(StatusCodes.Status404NotFound);
        }

        [HttpGet]
        [Route("UserExists")]
        public async Task<IActionResult> UserExists(string username)
        {
            var exist = await _db.Users.AnyAsync(x => x.UserName == username);
            if (exist)
            {
                return StatusCode(StatusCodes.Status200OK);
            }
            return StatusCode(StatusCodes.Status400BadRequest);
        }

        [HttpGet]
        [Route("EmailExists")]
        public async Task<IActionResult> EmailExists(string email)
        {
            var exist = await _db.Users.AnyAsync(x => x.Email == email);
            if (exist)
            {
                return StatusCode(StatusCodes.Status200OK);
            }
            return StatusCode(StatusCodes.Status400BadRequest);
        }

        [HttpGet]
        [Route("ForgetPassword/{email}")]
        public async Task<IActionResult> ForgetPassword(string email)
        {
            if (email == null)
            {
                return NotFound();
            }
            var user = await _manager.FindByEmailAsync(email);
            if (user == null)
            {
                return NotFound();
            }

            var token = await _manager.GeneratePasswordResetTokenAsync(user);
            var encodeToken = Encoding.UTF8.GetBytes(token);
            var newToken = WebEncoders.Base64UrlEncode(encodeToken);

            var confirmLink = $"http://localhost:4200/passwordconfirm?ID={user.Id}&Token={newToken}";
            var txt = "Please confirm password";
            var link = "<a href=\"" + confirmLink + "\">Passowrd confirm</a>";
            var title = "Passowrd confirm";
            if (await SendGridAPI.Execute(user.Email!, user.UserName!, txt, link, title))
            {
                return new ObjectResult(new { token = newToken });
            }

            return StatusCode(StatusCodes.Status400BadRequest);
        }

        [HttpPost]
        [Route("ResetPassword")]
        public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _manager.FindByIdAsync(model.Id);
                if (user == null)
                    return NotFound();

                var newToken = WebEncoders.Base64UrlDecode(model.Token);
                var encodeToken = Encoding.UTF8.GetString(newToken);

                var result = await _manager.ResetPasswordAsync(user, encodeToken, model.Password);
                if (result.Succeeded)
                {
                    return Ok();
                }
            }
            return BadRequest();
        }
    }
}