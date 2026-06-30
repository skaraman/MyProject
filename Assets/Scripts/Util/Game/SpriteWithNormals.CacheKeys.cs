using System;

public partial class SpriteWithNormals {
  readonly struct PairLookupCacheKey : IEquatable<PairLookupCacheKey> {
    public readonly string libraryName;
    public readonly string labelPrefix;
    public readonly string animation;
    public readonly int frame;

    public PairLookupCacheKey(string libraryName, string labelPrefix, string animation, int frame) {
      this.libraryName = libraryName ?? "";
      this.labelPrefix = labelPrefix ?? "";
      this.animation = animation ?? "";
      this.frame = frame;
    }

    public bool Equals(PairLookupCacheKey other) {
      return frame == other.frame &&
             string.Equals(libraryName, other.libraryName, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.Ordinal) &&
             string.Equals(animation, other.animation, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is PairLookupCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(libraryName ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(labelPrefix ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(animation ?? "");
        hash = (hash * 31) + frame;
        return hash;
      }
    }
  }

  readonly struct PaddedSpriteCacheKey : IEquatable<PaddedSpriteCacheKey> {
    public readonly ulong sourceSpriteId;
    public readonly int marginX;
    public readonly int marginY;
    public readonly bool useNormalFill;

    public PaddedSpriteCacheKey(ulong sourceSpriteId, int marginX, int marginY, bool useNormalFill) {
      this.sourceSpriteId = sourceSpriteId;
      this.marginX = marginX;
      this.marginY = marginY;
      this.useNormalFill = useNormalFill;
    }

    public bool Equals(PaddedSpriteCacheKey other) {
      return sourceSpriteId == other.sourceSpriteId &&
             marginX == other.marginX &&
             marginY == other.marginY &&
             useNormalFill == other.useNormalFill;
    }

    public override bool Equals(object obj) {
      return obj is PaddedSpriteCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = sourceSpriteId.GetHashCode();
        hash = (hash * 397) ^ marginX;
        hash = (hash * 397) ^ marginY;
        hash = (hash * 397) ^ (useNormalFill ? 1 : 0);
        return hash;
      }
    }
  }
}
