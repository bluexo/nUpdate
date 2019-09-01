﻿using System;

namespace nUpdate
{
    public interface IVersion : IComparable
    {
        bool IsValid();
        bool HasPreRelease { get; }
    }

    public interface IVersion<T> : IVersion, IComparable<T>, IEquatable<T>
    { }
}
