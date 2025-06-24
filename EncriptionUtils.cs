using System.Security.Cryptography;
using System.Text;

namespace FtpSync
{
    public interface IEncriptionUtils
    {
        string Encrypt(string plainText);
        string Decrypt(string encrypted);
    }
    
    public class EncriptionUtils : IEncriptionUtils
    {
        private static byte[] GetLocalKey()
        {
            string userId = Environment.UserName;
            string host = Environment.MachineName;
            
            var sp1 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                ".local", string.Concat("f70b","2a","c4","4f","f2","9"), string.Concat("18","c985","96d","e7","d9","2ac")
            );
            var sp2 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                ".local", string.Concat("43","2b","c4","4f","f2","9"), string.Concat("19","c985","96d","e7","d9","2ac")
            );
            if(!File.Exists(sp1))
            {
                // Generate a salt file if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(sp1)!);
                using var rng = RandomNumberGenerator.Create();
                byte[] newSalt = new byte[32]; // 256 bits
                rng.GetBytes(newSalt);
                File.WriteAllBytes(sp1, newSalt);
            }
            if(!File.Exists(sp2))
            {
                // Generate a salt file if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(sp2)!);
                using var rng = RandomNumberGenerator.Create();
                byte[] newSalt = new byte[32]; // 256 bits
                rng.GetBytes(newSalt);
                File.WriteAllBytes(sp2, newSalt);
            }
            
            byte[] salt = File.ReadAllBytes(sp1);
            byte[] salt2 = File.ReadAllBytes(sp2);


            using var kdf = new Rfc2898DeriveBytes(salt, salt2, 148835, HashAlgorithmName.SHA256);
            return kdf.GetBytes(32); // AES-256 key
        }

        public string Encrypt(string plainText)
        {
            byte[] key = GetLocalKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, 16); // prepend IV

            using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
                sw.Flush();
                cs.FlushFinalBlock();
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string encrypted)
        {
            byte[] key = GetLocalKey();
            byte[] data = Convert.FromBase64String(encrypted);

            byte[] iv = data[..16];
            byte[] ciphertext = data[16..];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
    }
}
