// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Internal
{
    /// <summary>
    ///     <para>
    ///         This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///         directly from your code. This API may change or be removed in future releases.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Scoped"/>. This means that each
    ///         <see cref="DbContext"/> instance will use its own instance of this service.
    ///         The implementation may depend on other services registered with any lifetime.
    ///         The implementation does not need to be thread-safe.
    ///     </para>
    /// </summary>
    public class ConcurrencyDetector : IConcurrencyDetector, IDisposable
    {
        private readonly IDisposable _disposer;

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        private int _inCriticalSection;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public ConcurrencyDetector() => _disposer = new Disposer(this);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IDisposable EnterCriticalSection()
        {
            if (Interlocked.CompareExchange(ref _inCriticalSection, 1, 0) == 1)
            {
                throw new InvalidOperationException(CoreStrings.ConcurrentMethodInvocation);
            }

            return _disposer;
        }

        private void ExitCriticalSection()
        {
            Debug.Assert(_inCriticalSection == 1, "Expected to be in a critical section");

            _inCriticalSection = 0;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual async Task<IDisposable> EnterCriticalSectionAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);

            return new AsyncDisposer(EnterCriticalSection(), this);
        }

        private readonly struct AsyncDisposer : IDisposable
        {
            private readonly IDisposable _disposable;
            private readonly ConcurrencyDetector _concurrencyDetector;

            public AsyncDisposer(IDisposable disposable, ConcurrencyDetector concurrencyDetector)
            {
                _disposable = disposable;
                _concurrencyDetector = concurrencyDetector;
            }

            public void Dispose()
            {
                _disposable.Dispose();

                if (_concurrencyDetector._semaphore == null)
                {
                    throw new ObjectDisposedException(GetType().ShortDisplayName(), CoreStrings.ContextDisposed);
                }

                _concurrencyDetector._semaphore.Release();
            }
        }

        private readonly struct Disposer : IDisposable
        {
            private readonly ConcurrencyDetector _concurrencyDetector;

            public Disposer(ConcurrencyDetector concurrencyDetector)
                => _concurrencyDetector = concurrencyDetector;

            public void Dispose() => _concurrencyDetector.ExitCriticalSection();
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void Dispose()
        {
            _semaphore?.Dispose();
            _semaphore = null;
        }
    }
}
