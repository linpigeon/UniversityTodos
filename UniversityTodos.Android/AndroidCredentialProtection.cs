using System;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace UniversityTodos.Android;

internal static class AndroidCredentialProtection
{
    private const string KeyAlias = "UniversityTodos.Credentials.v1";
    private const int GcmTagBits = 128;

    public static byte[] Protect(byte[] plain)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, GetKey());
        var iv = cipher.GetIV()!;
        var encrypted = cipher.DoFinal(plain)!;
        var result = new byte[1 + iv.Length + encrypted.Length];
        result[0] = checked((byte)iv.Length);
        Buffer.BlockCopy(iv, 0, result, 1, iv.Length);
        Buffer.BlockCopy(encrypted, 0, result, 1 + iv.Length, encrypted.Length);
        return result;
    }

    public static byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < 2) throw new ArgumentException("凭据密文格式无效", nameof(protectedData));
        var ivLength = protectedData[0];
        if (ivLength == 0 || protectedData.Length <= 1 + ivLength)
            throw new ArgumentException("凭据密文格式无效", nameof(protectedData));

        var iv = new byte[ivLength];
        Buffer.BlockCopy(protectedData, 1, iv, 0, ivLength);
        var encrypted = new byte[protectedData.Length - 1 - ivLength];
        Buffer.BlockCopy(protectedData, 1 + ivLength, encrypted, 0, encrypted.Length);

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, GetKey(), new GCMParameterSpec(GcmTagBits, iv));
        return cipher.DoFinal(encrypted)!;
    }

    private static IKey GetKey()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(KeyAlias)) return store.GetKey(KeyAlias, null)!;

        var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes("GCM")
            .SetEncryptionPaddings("NoPadding")
            .SetKeySize(256)
            .SetRandomizedEncryptionRequired(true)
            .Build());
        generator.GenerateKey();
        return store.GetKey(KeyAlias, null)!;
    }
}
