using System.Text.RegularExpressions;

namespace BorrowIt.Auth.Domain.Users.Policies
{
    public class MinimalEightLettersPasswordPolicy : IPasswordPolicy
    {
        public Regex Expression  => new Regex("^(?=.*[A-Za-z])(?=.*\\d)[A-Za-z\\d]{8,}$");
    }
}