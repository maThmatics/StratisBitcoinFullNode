﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Stratis.SmartContracts
{
    public class Message
    {
        internal Address ContractAddress { get; }

        public Address Sender { get; }

        public Gas GasLimit { get;  }

        public ulong Value { get; }

        public Message(Address contractAddress, Address sender, ulong value, Gas gasLimit)
        {
            ContractAddress = contractAddress;
            Sender = sender;
            Value = value;
            GasLimit = gasLimit;
        }
    }
}
