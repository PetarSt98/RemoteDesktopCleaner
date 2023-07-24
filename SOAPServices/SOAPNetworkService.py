#!/usr/bin/python
import sys
from suds.client import Client
from suds.sax.element import Element
from suds.xsd.doctor import ImportDoctor, Import
from ldap3 import Server, Connection, ALL
from pprint import pprint
from suds.sudsobject import asdict

# Function to convert suds object into serializable format.
def recursive_asdict(d):
    out = {}
    for k, v in asdict(d).items():
        if hasattr(v, '__keylist__'):
            out[k] = recursive_asdict(v)
        elif isinstance(v, list):
            out[k] = []
            for item in v:
                if hasattr(item, '__keylist__'):
                    out[k].append(recursive_asdict(item))
                else:
                    out[k].append(item)
        else:
            out[k] = v
    return out

# Client setup
url = 'https://network.cern.ch/sc/soap/soap.fcgi?v=6&WSDL'
imp = Import('http://schemas.xmlsoap.org/soap/encoding/')
doc = ImportDoctor(imp)
client = Client(url, doctor=doc, cache=None)

# Authentication
username = sys.argv[2] if len(sys.argv) > 2 else exit("Please specify the username")
password = sys.argv[3] if len(sys.argv) > 3 else exit("Please specify the password")
token = client.service.getAuthToken(username, password, 'CERN')
authenticationHeader = Element('Auth').insert(Element('token').setText(token))
client.set_options(soapheaders=authenticationHeader)

# Calling getDeviceInfo
deviceName = sys.argv[1] if len(sys.argv) > 1 else exit("Please specify the set name")
result = client.service.getDeviceInfo(deviceName)

# Convert the result to a dictionary
result_dict = recursive_asdict(result)

# Add new elements to the dictionary
result_dict["NetworkDomainName"] = result_dict["Interfaces"]["NetworkDomainName"] if "NetworkDomainName" in result_dict["Interfaces"] else None
result_dict["ResponsiblePersonName"] = result_dict["ResponsiblePerson"]["Name"] if "Name" in result_dict["ResponsiblePerson"] else None
result_dict["ResponsiblePersonEmail"] = result_dict["ResponsiblePerson"]["Email"] if "Email" in result_dict["ResponsiblePerson"] else None

# Define LDAP server and base DN
ldap_server = Server('ldap.cern.ch', get_info=ALL)
base_dn = 'OU=Users,OU=Organic Units,DC=cern,DC=ch'

# # Perform LDAP search for the responsible person
# with Connection(ldap_server) as conn:
#     conn.search(search_base=base_dn,
#                 search_filter='(&(objectClass=user)(mail={}))'.format(result_dict["ResponsiblePersonEmail"]),
#                 attributes=['cn'])
#     if conn.entries:
#         owner_info = conn.entries[0]['cn'].value
#         result_dict["ResponsiblePersonUsername"] = owner_info
#     else:
#         result_dict["ResponsiblePersonUsername"] = None

pprint(result_dict)
