using System.Security.Cryptography;
using System.Text;

namespace FtpSync
{
    public class DPAPIUtils
    {
#pragma warning disable CA1416 // Convalida compatibilità della piattaforma
        private static string Protect(string stringToEncrypt, string optionalEntropy)
        {
            return Convert.ToBase64String(
                ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(stringToEncrypt)
                    , optionalEntropy != null ? Encoding.UTF8.GetBytes(optionalEntropy) : null
                    , DataProtectionScope.LocalMachine));
        }

        private static string Unprotect(string encryptedString, string optionalEntropy)
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedString)
                    , optionalEntropy != null ? Encoding.UTF8.GetBytes(optionalEntropy) : null
                    , DataProtectionScope.LocalMachine));
        }
#pragma warning restore CA1416 // Convalida compatibilità della piattaforma
        public static string Encrypt(string stringToEncrypt, string optionalEntropy)
        {
            string encEnt = string.Empty;
            string encVal = string.Empty;
            do
            {
                encEnt = DPAPIUtils.Protect(optionalEntropy, "likw@#$%ebnfFQRF4WGcoi134b87FBOY3!@#%$@%");
                encVal = DPAPIUtils.Protect(stringToEncrypt, optionalEntropy);
            } while (encEnt.Contains("qq") || encVal.Contains("qq"));
            var encrypted = $"{encVal}qq{encEnt}";

            do
            {
                encEnt = DPAPIUtils.Protect(optionalEntropy, "sdahfbQFWRA$UHGFPWF$#@%($#!&^!(_$^&%");
                encVal = DPAPIUtils.Protect(encrypted, optionalEntropy);
            } while (encEnt.Contains("qqzz") || encVal.Contains("qqzz"));
            return $"{encVal}qqzz{encEnt}";
        }

        public static string Decrypt(string stringToDecrypt)
        {
            var val = stringToDecrypt.Split("qqzz")[0];
            var entr = stringToDecrypt.Split("qqzz")[1];
            var visEntr = Unprotect(entr, "sdahfbQFWRA$UHGFPWF$#@%($#!&^!(_$^&%");
            var decr1 = Unprotect(val, visEntr);

            val = decr1.Split("qq")[0];
            entr = decr1.Split("qq")[1];
            visEntr = Unprotect(entr, "likw@#$%ebnfFQRF4WGcoi134b87FBOY3!@#%$@%");
            return Unprotect(val, visEntr);
        }
    }
}
