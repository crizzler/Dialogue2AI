using System.Security.Cryptography;
using System.Text;

namespace ImmersiveNPCs
{
    public static class CacheKeyHasher
    {
        public static string ComputeHash(string input)
        {
            if (input == null)
            {
                input = string.Empty;
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
