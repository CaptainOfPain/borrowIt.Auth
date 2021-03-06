using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BorrowIt.Auth.Application.DTOs;
using BorrowIt.Auth.Domain.Users;
using BorrowIt.Auth.Domain.Users.DataStructure;
using BorrowIt.Auth.Domain.Users.Events;
using BorrowIt.Auth.Domain.Users.Factories;
using BorrowIt.Auth.Infrastructure.Repositories.Users;
using BorrowIt.Common.Exceptions;
using BorrowIt.Common.Rabbit.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BorrowIt.Auth.Application.Services
{
    public class UsersService : IUsersService
    {
        private readonly IUsersRepository _usersRepository;
        private readonly IUserFactory _userFactory;
        private readonly IBusPublisher _busPublisher;
        private readonly IConfiguration _configuration;

        public UsersService(IUsersRepository usersRepository, IUserFactory userFactory, IBusPublisher busPublisher, IConfiguration configuration)
        {
            _usersRepository = usersRepository;
            _userFactory = userFactory;
            _busPublisher = busPublisher;
            _configuration = configuration;
        }
        
        public async Task AddUserAsync(UserDataStructure userDataStructure)
        {
            var user = (await _usersRepository.GetWithExpressionAsync(x => x.UserName == userDataStructure.UserName)).SingleOrDefault();

            if (user != null)
            {
                throw new BusinessLogicException("User already exists");
            }
            
            user = _userFactory.CreateUser(userDataStructure);
            ValidatePasswords(userDataStructure.Password, userDataStructure.ConfirmPassword);
            user.SetPassword(userDataStructure.Password);

            await _usersRepository.CreateAsync(user);

            var userCratedEvent = new UserChangedEvent(user.Id, user.UserName, user.Email,
                user.Roles.Select(x => x.ToString()), user.FirstName, user.SecondName, user.BirthDate, user.ModifyDate,
                new AddressEventData()
                {
                    City = user.Address.City,
                    PostalCode = user.Address.City,
                    Street = user.Address.Street
                });

            await _busPublisher.PublishAsync(userCratedEvent);
        }

        public async Task UpdateUserAsync(UserDataStructure userDataStructure)
        {
            if (userDataStructure?.Id == null)
            {
                throw new BusinessLogicException("Missing id");
            }

            var user = await GetOneOrThrowAsync(userDataStructure.Id.Value);
            
            user.UpdateUser(userDataStructure.Email,
                userDataStructure.FirstName,
                userDataStructure.SecondName,
                userDataStructure.BirthDate, new Address(userDataStructure.PostalCode, userDataStructure.Street, userDataStructure.City));

            await _usersRepository.UpdateAsync(user);
            
            var userCratedEvent = new UserChangedEvent(user.Id, user.UserName, user.Email,
                user.Roles.Select(x => x.ToString()), user.FirstName, user.SecondName, user.BirthDate, user.ModifyDate,
                new AddressEventData()
                {
                    City = user.Address.City,
                    PostalCode = user.Address.City,
                    Street = user.Address.Street
                });

            await _busPublisher.PublishAsync(userCratedEvent);
        }

        public async Task RemoveUserAsync(Guid id)
        {
            var user = await GetOneOrThrowAsync(id);
            await _usersRepository.RemoveAsync(user);

            await _busPublisher.PublishAsync(new UserRemovedEvent(id));
        }

        public async Task SetPasswordAsync(string userName, string password, string confirmPassword)
        {
            var user = await GetOneOrThrowAsync(userName);
            ValidatePasswords(password, confirmPassword);
            user.SetPassword(password);

            await _usersRepository.UpdateAsync(user);
        }

        public async Task ChangePasswordAsync(string userName, string oldPassword, string newPassword, string confirmPassword)
        {
            var user = await GetOneOrThrowAsync(userName);

            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            {
                throw new BusinessLogicException("Invalid password");
            }
            
            ValidatePasswords(newPassword, confirmPassword);
            user.SetPassword(newPassword);

            await _usersRepository.UpdateAsync(user);
        }

        public async Task<UserSignedInDto> SignInAsync(UserDataStructure userDataStructure)
        {
            var user = await GetOneOrThrowAsync(userDataStructure.UserName);

            if (!BCrypt.Net.BCrypt.Verify(userDataStructure.Password, user.PasswordHash))
            {
                throw new BusinessLogicException("Invalid credentials.");
            }

            return new UserSignedInDto()
            {
                Token = GenerateToken(user),
                Email = user.Email,
                Id = user.Id.ToString(),
                UserName = user.UserName
            };
        }

        private async Task<User> GetOneOrThrowAsync(Guid id)
        {
            var user = await _usersRepository.GetAsync(id);
            if (user == null)
            {
                throw new BusinessLogicException("User doesn't exist");
            }

            return user;
        }

        private string GenerateToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Secret"]);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[] 
                {
                    new Claim(ClaimTypes.Name, user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }

        private async Task<User> GetOneOrThrowAsync(string userName)
        {
            var user = (await _usersRepository.GetWithExpressionAsync(x => x.UserName == userName)).SingleOrDefault();
            if (user == null)
            {
                throw new BusinessLogicException("User doesn't exist");
            }

            return user;
        }

        private void ValidatePasswords(string password, string confirmPassword)
        {
            if (!password.Equals(confirmPassword))
            {
                throw new BusinessLogicException("Passwords are not equal");
            }
        }
    }
}