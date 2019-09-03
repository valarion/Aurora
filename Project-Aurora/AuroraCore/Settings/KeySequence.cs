﻿using Aurora.Devices.Layout;
using Aurora.Devices.Layout.Layouts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Aurora.Settings
{
    /// <summary>
    /// The type of the KeySequence
    /// </summary>
    public enum KeySequenceType
    {
        /// <summary>
        /// Sequence uses an array of DeviceKeys keys
        /// </summary>
        Sequence,
        /// <summary>
        /// Sequence uses a freeform region
        /// </summary>
        FreeForm
    }

    /// <summary>
    /// A class representing a series of DeviceKeys keys or a freeform region
    /// </summary>
    public class KeySequence : ICloneable
    {
        /// <summary>
        /// An array of DeviceKeys keys to be used with KeySequenceType.Sequence type.
        /// </summary>
        public List<DeviceLED> keys;

        /// <summary>
        /// The type of this KeySequence instance.
        /// </summary>
        public KeySequenceType type;

        /// <summary>
        /// The Freeform object to be used with KeySequenceType.FreeForm type
        /// </summary>
        public FreeFormObject freeform;

        public KeySequence()
        {
            keys = new List<DeviceLED>();
            type = KeySequenceType.Sequence;
            freeform = new FreeFormObject();
        }

        public KeySequence(KeySequence otherKeysequence)
        {
            this.keys = new List<DeviceLED>(otherKeysequence.keys);
            type = otherKeysequence.type;
            this.freeform = otherKeysequence.freeform;
        }

        public KeySequence(FreeFormObject freeform)
        {
            this.keys = new List<DeviceLED>();
            type = KeySequenceType.FreeForm;
            this.freeform = freeform;
        }

        public KeySequence(IEnumerable<DeviceLED> keys)
        {
            this.keys = new List<DeviceLED>(keys);
            type = KeySequenceType.Sequence;
            freeform = new FreeFormObject();
        }

        public KeySequence(IEnumerable<KeyboardKeys> keys)
        {
            this.keys = keys.ToList().ConvertAll(s => s.GetDeviceLED());
            type = KeySequenceType.Sequence;
            freeform = new FreeFormObject();
        }

        public RectangleF GetAffectedRegion()
        {
            switch (type)
            {
                case KeySequenceType.FreeForm:
                    return new RectangleF(LayoutUtils.PixelToByte(this.freeform.X), LayoutUtils.PixelToByte(this.freeform.Y), LayoutUtils.PixelToByte(this.freeform.Width), LayoutUtils.PixelToByte(this.freeform.Height));
                default:

                    float left = 0.0f;
                    float top = left;
                    float right = top;
                    float bottom = right;

                    foreach (DeviceLED key in this.keys)
                    {
                        BitmapRectangle keyMapping = GlobalDeviceLayout.Instance.GetDeviceLEDBitmapRegion(key);

                        if (left == top && top == right && right == bottom && bottom == 0.0f)
                        {
                            left = keyMapping.Left;
                            top = keyMapping.Top;
                            right = keyMapping.Right;
                            bottom = keyMapping.Bottom;
                        }
                        else
                        {
                            if (keyMapping.Left < left)
                                left = keyMapping.Left;
                            if (keyMapping.Top < top)
                                top = keyMapping.Top;
                            if (keyMapping.Right > right)
                                right = keyMapping.Right;
                            if (keyMapping.Bottom > bottom)
                                bottom = keyMapping.Bottom;
                        }
                    }

                    return new RectangleF(left, top, (right - left), (bottom - top));
            }

        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((KeySequence)obj);
        }

        public bool Equals(KeySequence p)
        {
            if (ReferenceEquals(null, p)) return false;
            if (ReferenceEquals(this, p)) return true;

            return (new HashSet<DeviceLED>(keys).SetEquals(p.keys)) &&
                type == p.type &&
                freeform.Equals(p.freeform);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + keys.GetHashCode();
                hash = hash * 23 + type.GetHashCode();
                hash = hash * 23 + freeform.GetHashCode();
                return hash;
            }
        }

        public object Clone()
        {
            return new KeySequence(this);
        }
    }
}