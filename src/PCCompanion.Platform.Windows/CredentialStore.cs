using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Secrets in Windows Credential Manager, as generic credentials for this PC only
/// (<c>CRED_PERSIST_LOCAL_MACHINE</c>): Windows protects them with the user's sign-in (DPAPI), they
/// don't roam with the profile, and the user can see and remove them in Credential Manager. A
/// secret is kept as UTF-16 text, as Windows keeps passwords, and the copies PC Companion makes of
/// it in memory are cleared after use, except the string <see cref="Read"/> returns.
/// </summary>
public static class CredentialStore
{
    /// <summary>The longest secret, in UTF-16 code units: Credential Manager keeps 2,560 bytes at most.</summary>
    public const int MaximumSecretLength = 2_560 / sizeof(char);

    /// <summary>The longest comment Credential Manager keeps, in characters.</summary>
    public const int MaximumCommentLength = 255;

    /// <summary>Stores <paramref name="secret"/> under <paramref name="target"/>, replacing what was there.</summary>
    /// <param name="target">The credential's name, such as <c>PCCompanion/RemoteFolders/&lt;ID&gt;</c>.</param>
    /// <param name="userName">The user name Credential Manager shows with it.</param>
    /// <param name="comment">What Credential Manager shows as the credential's comment, at most <see cref="MaximumCommentLength"/> characters.</param>
    /// <exception cref="Win32Exception">Windows refused, for example in a session without the user's profile.</exception>
    public static unsafe void Write(string target, string userName, string secret, string comment)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secret.Length, MaximumSecretLength, nameof(secret));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(comment.Length, MaximumCommentLength, nameof(comment));

        byte[] blob = Encoding.Unicode.GetBytes(secret);
        try
        {
            fixed (char* targetName = target)
            fixed (char* user = userName)
            fixed (char* text = comment)
            fixed (byte* bytes = blob)
            {
                var credential = new CREDENTIALW
                {
                    Type = CRED_TYPE.CRED_TYPE_GENERIC,
                    TargetName = targetName,
                    UserName = user,
                    Comment = text,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = bytes,
                    Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
                };
                if (!PInvoke.CredWrite(credential, 0))
                {
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    /// <summary>The secret stored under <paramref name="target"/>, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="Win32Exception">Windows couldn't read it.</exception>
    public static unsafe string? Read(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        CREDENTIALW* credential;
        bool read;
        fixed (char* name = target)
        {
            read = PInvoke.CredRead(name, CRED_TYPE.CRED_TYPE_GENERIC, 0, &credential);
        }

        if (!read)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == (int)WIN32_ERROR.ERROR_NOT_FOUND ? null : throw new Win32Exception(error);
        }

        try
        {
            return new string((char*)credential->CredentialBlob, 0, (int)(credential->CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            NativeMemory.Clear(credential->CredentialBlob, credential->CredentialBlobSize);
            PInvoke.CredFree(credential);
        }
    }

    /// <summary>Deletes the credential stored under <paramref name="target"/>; returns whether there was one.</summary>
    /// <exception cref="Win32Exception">Windows couldn't delete it.</exception>
    public static unsafe bool Delete(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        bool deleted;
        fixed (char* name = target)
        {
            deleted = PInvoke.CredDelete(name, CRED_TYPE.CRED_TYPE_GENERIC, 0);
        }

        if (deleted)
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        return error == (int)WIN32_ERROR.ERROR_NOT_FOUND ? false : throw new Win32Exception(error);
    }

    /// <summary>The names of the user's generic credentials that start with <paramref name="prefix"/>.</summary>
    /// <exception cref="Win32Exception">Windows couldn't list them.</exception>
    public static unsafe IReadOnlyList<string> Targets(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        uint count;
        CREDENTIALW** credentials;
        bool listed;
        fixed (char* filter = prefix + "*")
        {
            listed = PInvoke.CredEnumerate(filter, 0, &count, &credentials);
        }

        if (!listed)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == (int)WIN32_ERROR.ERROR_NOT_FOUND ? [] : throw new Win32Exception(error);
        }

        try
        {
            var targets = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                if (credentials[i]->Type == CRED_TYPE.CRED_TYPE_GENERIC)
                {
                    targets.Add(credentials[i]->TargetName.ToString());
                }
            }

            return targets;
        }
        finally
        {
            PInvoke.CredFree(credentials);
        }
    }
}
