﻿//  Copyright 2020 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using NtApiDotNet.Win32.Security.Buffers;
using NtApiDotNet.Win32.Security.Native;
using System;
using System.Collections.Generic;

namespace NtApiDotNet.Win32.Security.Authentication
{
    /// <summary>
    /// Class to represent a client authentication context.
    /// </summary>
    public sealed class ClientAuthenticationContext : IDisposable, IAuthenticationContext, IClientAuthenticationContext
    {
        private readonly CredentialHandle _creds;
        private readonly InitializeContextReqFlags _req_attributes;
        private readonly SecHandle _context;
        private readonly string _target;
        private readonly SecDataRep _data_rep;
        private readonly byte[] _channel_binding;
        private int _token_count;

        /// <summary>
        /// The current authentication token.
        /// </summary>
        public AuthenticationToken Token { get; private set; }

        /// <summary>
        /// Whether the authentication is done.
        /// </summary>
        public bool Done { get; private set; }

        /// <summary>
        /// Current status flags.
        /// </summary>
        public InitializeContextRetFlags Flags { get; private set; }

        /// <summary>
        /// Expiry of the authentication.
        /// </summary>
        public long Expiry { get; private set; }

        /// <summary>
        /// Get the Session Key for this context.
        /// </summary>
        public byte[] SessionKey => ServerAuthenticationContext.GetSessionKey(_context);

        /// <summary>
        /// Get the maximum signature size of this context.
        /// </summary>
        public int MaxSignatureSize => SecurityContextUtils.GetMaxSignatureSize(_context);

        /// <summary>
        /// Get the size of the security trailer for this context.
        /// </summary>
        public int SecurityTrailerSize => SecurityContextUtils.GetSecurityTrailerSize(_context);

        /// <summary>
        /// Get the last token status for the client context.
        /// </summary>
        public SecPkgLastClientTokenStatus LastTokenStatus => SecurityContextUtils.QueryContextAttribute<SecPkgContext_LastClientTokenStatus>(_context, 
            SECPKG_ATTR.LAST_CLIENT_TOKEN_STATUS).LastClientTokenStatus;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        /// <param name="req_attributes">Request attribute flags.</param>
        /// <param name="target">Target SPN (optional).</param>
        /// <param name="data_rep">Data representation.</param>
        /// <param name="channel_binding">Optional channel binding token.</param>
        public ClientAuthenticationContext(CredentialHandle creds, 
            InitializeContextReqFlags req_attributes,
            string target, byte[] channel_binding, SecDataRep data_rep)
        {
            _creds = creds;
            _req_attributes = req_attributes & ~InitializeContextReqFlags.AllocateMemory;
            _context = new SecHandle();
            _target = target == string.Empty ? null : target;
            _data_rep = data_rep;
            _token_count = 0;
            _channel_binding = channel_binding;
            Continue(null);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        /// <param name="req_attributes">Request attribute flags.</param>
        /// <param name="target">Target SPN (optional).</param>
        /// <param name="data_rep">Data representation.</param>
        public ClientAuthenticationContext(CredentialHandle creds,
            InitializeContextReqFlags req_attributes,
            string target, SecDataRep data_rep)
            : this(creds, req_attributes, target, null, data_rep)
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        /// <param name="req_attributes">Request attribute flags.</param>
        /// <param name="data_rep">Data representation.</param>
        public ClientAuthenticationContext(CredentialHandle creds, InitializeContextReqFlags req_attributes, SecDataRep data_rep)
            : this(creds, req_attributes, null, null, data_rep)
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creds">Credential handle.</param>
        public ClientAuthenticationContext(CredentialHandle creds)
            : this(creds, InitializeContextReqFlags.None, null, SecDataRep.Native)
        {
        }

        /// <summary>
        /// Continue the authentication with the server token.
        /// </summary>
        /// <param name="token">The server token to continue authentication.</param>
        public void Continue(AuthenticationToken token)
        {
            Done = GenClientContext(token);
        }

        /// <summary>
        /// Make a signature for this context.
        /// </summary>
        /// <param name="messages">The message buffers to sign.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>The signature blob.</returns>
        public byte[] MakeSignature(IEnumerable<SecurityBuffer> messages, int sequence_no)
        {
            return SecurityContextUtils.MakeSignature(_context, 0, messages, sequence_no);
        }

        /// <summary>
        /// Make a signature for this context.
        /// </summary>
        /// <param name="message">The message to sign.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>The signature blob.</returns>
        public byte[] MakeSignature(byte[] message, int sequence_no)
        {
            return SecurityContextUtils.MakeSignature(_context, 0, message, sequence_no);
        }

        /// <summary>
        /// Verify a signature for this context.
        /// </summary>
        /// <param name="message">The message to verify.</param>
        /// <param name="signature">The signature blob for the message.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>True if the signature is valid, otherwise false.</returns>
        public bool VerifySignature(byte[] message, byte[] signature, int sequence_no)
        {
            return SecurityContextUtils.VerifySignature(_context, message, signature, sequence_no);
        }

        /// <summary>
        /// Verify a signature for this context.
        /// </summary>
        /// <param name="messages">The messages to verify.</param>
        /// <param name="signature">The signature blob for the message.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>True if the signature is valid, otherwise false.</returns>
        public bool VerifySignature(IEnumerable<SecurityBuffer> messages, byte[] signature, int sequence_no)
        {
            return SecurityContextUtils.VerifySignature(_context, messages, signature, sequence_no);
        }

        /// <summary>
        /// Encrypt a message for this context.
        /// </summary>
        /// <param name="message">The message to encrypt.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>The encrypted message.</returns>
        public EncryptedMessage EncryptMessage(byte[] message, int sequence_no)
        {
            return SecurityContextUtils.EncryptMessage(_context, 0, message, sequence_no);
        }

        /// <summary>
        /// Encrypt a message for this context.
        /// </summary>
        /// <param name="messages">The messages to encrypt.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>The signature for the messages.</returns>
        /// <remarks>The messages are encrypted in place. You can add buffers with the ReadOnly flag to prevent them being encrypted.</remarks>
        public byte[] EncryptMessage(IEnumerable<SecurityBuffer> messages, int sequence_no)
        {
            return SecurityContextUtils.EncryptMessage(_context, 0, messages, sequence_no);
        }

        /// <summary>
        /// Decrypt a message for this context.
        /// </summary>
        /// <param name="message">The message to decrypt.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <returns>The decrypted message.</returns>
        public byte[] DecryptMessage(EncryptedMessage message, int sequence_no)
        {
            return SecurityContextUtils.DecryptMessage(_context, message, sequence_no);
        }

        /// <summary>
        /// Decrypt a message for this context.
        /// </summary>
        /// <param name="messages">The messages to decrypt.</param>
        /// <param name="sequence_no">The sequence number.</param>
        /// <param name="signature">The signature for the messages.</param>
        /// <remarks>The messages are decrypted in place. You can add buffers with the ReadOnly flag to prevent them being decrypted.</remarks>
        public void DecryptMessage(IEnumerable<SecurityBuffer> messages, byte[] signature, int sequence_no)
        {
            SecurityContextUtils.DecryptMessage(_context, messages, signature, sequence_no);
        }

        /// <summary>
        /// Query the context's package info.
        /// </summary>
        /// <returns>The authentication package info,</returns>
        public AuthenticationPackage GetAuthenticationPackage()
        {
            return SecurityContextUtils.GetAuthenticationPackage(_context);
        }

        /// <summary>
        /// Get the name of the authentication package.
        /// </summary>
        public string PackageName => SecurityContextUtils.GetPackageName(_context) ?? _creds.PackageName;

        private bool GenClientContext(AuthenticationToken token)
        {
            using (DisposableList list = new DisposableList())
            {
                SecStatusCode result = 0;

                SecBuffer out_sec_buffer = list.AddResource(new SecBuffer(SecurityBufferType.Token, 64 * 1024));
                SecBufferDesc out_buffer_desc = list.AddResource(new SecBufferDesc(out_sec_buffer));

                InitializeContextRetFlags flags;
                LargeInteger expiry = new LargeInteger();
                if (token != null)
                {
                    List<SecBuffer> buffers = new List<SecBuffer>();
                    buffers.Add(list.AddResource(new SecBuffer(SecurityBufferType.Token, token.ToArray())));
                    if (_channel_binding != null)
                    {
                        buffers.Add(list.AddResource(SecBuffer.CreateForChannelBinding(_channel_binding)));
                    }
                    SecBufferDesc in_buffer_desc = list.AddResource(new SecBufferDesc(buffers.ToArray()));
                    result = SecurityNativeMethods.InitializeSecurityContext(_creds.CredHandle, _context, _target, _req_attributes, 0,
                        _data_rep, in_buffer_desc, 0, _context, out_buffer_desc, out flags, expiry).CheckResult();
                    Flags = flags;
                }
                else
                {
                    SecBufferDesc in_buffer_desc = null;
                    List<SecBuffer> buffers = new List<SecBuffer>();
                    if (_channel_binding != null)
                    {
                        buffers.Add(list.AddResource(SecBuffer.CreateForChannelBinding(_channel_binding)));
                        in_buffer_desc = list.AddResource(new SecBufferDesc(buffers.ToArray()));
                    }

                    result = SecurityNativeMethods.InitializeSecurityContext(_creds.CredHandle, null, _target,
                        _req_attributes, 0, _data_rep, in_buffer_desc, 0, _context,
                        out_buffer_desc, out flags, expiry).CheckResult();
                }

                Expiry = expiry.QuadPart;
                Flags = flags;
                if (result == SecStatusCode.CompleteNeeded || result == SecStatusCode.CompleteAndContinue)
                {
                    SecurityNativeMethods.CompleteAuthToken(_context, out_buffer_desc).CheckResult();
                }

                Token = AuthenticationToken.Parse(_creds.PackageName, _token_count++, true, out_buffer_desc.ToArray()[0].ToArray());
                return !(result == SecStatusCode.ContinueNeeded || result == SecStatusCode.CompleteAndContinue);
            }
        }

        private void Dispose(bool _)
        {
            SecurityNativeMethods.DeleteSecurityContext(_context);
        }

        /// <summary>
        /// Dispose the client context.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~ClientAuthenticationContext()
        {
            Dispose(false);
        }
    }
}
