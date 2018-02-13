﻿using Certify.Models.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CredentialsManager
    {
        public const string CREDENTIALSTORE = "cred";

        public string StorageSubfolder = "credentials"; //if specified will be appended to AppData path as subfolder to load/save to
        private const string PROTECTIONENTROPY = "Certify.Credentials";

        private string GetDbPath()
        {
            string appDataPath = Util.GetAppDataFolder(StorageSubfolder);
            return Path.Combine(appDataPath, $"{CREDENTIALSTORE}.db");
        }

        /// <summary>
        /// Delete credential by key. This will fail if the credential is currently in use. 
        /// </summary>
        /// <param name="storageKey"></param>
        /// <returns></returns>
        public async Task<bool> DeleteCredential(string storageKey)
        {
            var inUse = await IsCredentialInUse(storageKey);

            if (!inUse)
            {
                //delete credential in database
                var path = GetDbPath();

                if (File.Exists(path))
                {
                    using (var db = new SQLiteConnection($"Data Source={path}"))
                    {
                        await db.OpenAsync();
                        using (var tran = db.BeginTransaction())
                        {
                            using (var cmd = new SQLiteCommand("DELETE FROM credential WHERE id=@id", db))
                            {
                                cmd.Parameters.Add(new SQLiteParameter("@id", storageKey));
                                await cmd.ExecuteNonQueryAsync();
                            }
                            tran.Commit();
                        }
                        db.Close();
                    }
                }

                return true;
            }
            else
            {
                //could not delete
                return false;
            }
        }

        public async Task<bool> IsCredentialInUse(string storageKey)
        {
            var managedSites = await new ItemManager().GetManagedSites(new Models.ManagedSiteFilter { StoredCredentialKey = storageKey });
            if (managedSites.Any())
            {
                // credential is in use
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Get protected version of a secret 
        /// </summary>
        /// <param name="clearText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        private string Protect(
                string clearText,
                string optionalEntropy = null,
                DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (clearText == null) return null;
            byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
            byte[] entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                ? null
                : Encoding.UTF8.GetBytes(optionalEntropy);
            byte[] encryptedBytes = ProtectedData.Protect(clearBytes, entropyBytes, scope);
            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// Get unprotected version of a secret 
        /// </summary>
        /// <param name="encryptedText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        private string Unprotect(
            string encryptedText,
            string optionalEntropy = null,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (encryptedText == null)
                throw new ArgumentNullException("encryptedText");
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                ? null
                : Encoding.UTF8.GetBytes(optionalEntropy);
            byte[] clearBytes = ProtectedData.Unprotect(encryptedBytes, entropyBytes, scope);
            return Encoding.UTF8.GetString(clearBytes);
        }

        /// <summary>
        /// Return summary list of stored credentials (excluding secrets) for given type 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<List<StoredCredential>> GetStoredCredentials()
        {
            var path = GetDbPath();

            if (File.Exists(path))
            {
                List<StoredCredential> credentials = new List<StoredCredential>();

                using (var db = new SQLiteConnection($"Data Source={path}"))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("SELECT id, json FROM credential", db))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var storedCredential = JsonConvert.DeserializeObject<StoredCredential>((string)reader["json"]);
                            credentials.Add(storedCredential);
                        }
                    }
                    db.Close();
                }

                return credentials;
            }
            else
            {
                return new List<StoredCredential>();
            }
        }

        public async Task<string> GetUnlockedCredential(string storageKey)
        {
            string protectedString = null;

            var path = GetDbPath();

            //load protected string from db
            if (File.Exists(path))
            {
                using (var db = new SQLiteConnection($"Data Source={path}"))
                using (var cmd = new SQLiteCommand("SELECT json, protectedvalue FROM credential WHERE id=@id", db))
                {
                    cmd.Parameters.Add(new SQLiteParameter("@id", storageKey));

                    db.Open();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var storedCredential = JsonConvert.DeserializeObject<StoredCredential>((string)reader["json"]);
                            protectedString = (string)reader["protectedvalue"];
                        }
                    }
                    db.Close();
                }
            }

            return Unprotect(protectedString, PROTECTIONENTROPY, DataProtectionScope.CurrentUser);
        }

        public async Task<bool> UpdateCredential(StoredCredential credentialInfo)
        {
            if (credentialInfo.Secret == null) return false;

            credentialInfo.DateCreated = DateTime.Now;

            string protectedContent = Protect(credentialInfo.Secret, PROTECTIONENTROPY, DataProtectionScope.CurrentUser);

            var path = GetDbPath();

            //create database if it doesn't exist
            if (!File.Exists(path))
            {
                using (var db = new SQLiteConnection($"Data Source={path}"))
                {
                    await db.OpenAsync();
                    using (var cmd = new SQLiteCommand("CREATE TABLE credential (id TEXT NOT NULL UNIQUE PRIMARY KEY, json TEXT NOT NULL, protectedvalue TEXT NOT NULL)", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // save new/modified item into credentials database
            using (var db = new SQLiteConnection($"Data Source={path}"))
            {
                await db.OpenAsync();
                using (var tran = db.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO credential (id, json, protectedvalue) VALUES (@id, @json, @protectedvalue)", db))
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@id", credentialInfo.StorageKey));
                        cmd.Parameters.Add(new SQLiteParameter("@json", JsonConvert.SerializeObject(credentialInfo)));
                        cmd.Parameters.Add(new SQLiteParameter("@protectedvalue", protectedContent));
                        await cmd.ExecuteNonQueryAsync();
                    }

                    tran.Commit();
                }
                db.Close();
            }
            return true;
        }
    }
}