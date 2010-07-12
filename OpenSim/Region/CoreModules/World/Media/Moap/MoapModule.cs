/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.IO;
using System.Web;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Messages.Linden;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

namespace OpenSim.Region.CoreModules.Media.Moap
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "MoapModule")]
    public class MoapModule : INonSharedRegionModule, IMoapModule
    {    
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        public string Name { get { return "MoapModule"; } }                
        public Type ReplaceableInterface { get { return null; } }        
        
        protected Scene m_scene;
        
        public void Initialise(IConfigSource config) 
        {
            // TODO: Add config switches to enable/disable this module
        }

        public void AddRegion(Scene scene) 
        { 
            m_scene = scene;
        }

        public void RemoveRegion(Scene scene) {}

        public void RegionLoaded(Scene scene) 
        {
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
        }
        
        public void Close() 
        {
            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
        }        
        
        public void RegisterCaps(UUID agentID, Caps caps)
        {
            m_log.DebugFormat(
                "[MOAP]: Registering ObjectMedia and ObjectMediaNavigate capabilities for agent {0}", agentID);
            
            // We do receive a post to ObjectMedia when a new avatar enters the region - though admittedly this is the
            // avatar that set the texture in the first place.
            // Even though we're registering for POST we're going to get GETS and UPDATES too
            caps.RegisterHandler(
                "ObjectMedia", new RestStreamHandler("POST", "/CAPS/" + UUID.Random(), HandleObjectMediaMessage));
            
            // We do get these posts when the url has been changed.
            // Even though we're registering for POST we're going to get GETS and UPDATES too
            caps.RegisterHandler(
                "ObjectMediaNavigate", new RestStreamHandler("POST", "/CAPS/" + UUID.Random(), HandleObjectMediaNavigateMessage));
        }      
        
        public MediaEntry GetMediaEntry(SceneObjectPart part, int face)
        {
            if (face < 0)
                throw new ArgumentException("Face cannot be less than zero");
            
            List<MediaEntry> media = part.Shape.Media;           
            
            if (face > media.Count - 1)
                throw new ArgumentException(
                    string.Format("Face argument was {0} but max is {1}", face, media.Count - 1));
            
            // TODO: Really need a proper copy constructor down in libopenmetaverse
            return MediaEntry.FromOSD(media[face].GetOSD());                        
        }
        
        /// <summary>
        /// Sets or gets per face media textures.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="httpRequest"></param>
        /// <param name="httpResponse"></param>
        /// <returns></returns>
        protected string HandleObjectMediaMessage(
            string request, string path, string param, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {            
            m_log.DebugFormat("[MOAP]: Got ObjectMedia raw request [{0}]", request);
         
            OSDMap osd = (OSDMap)OSDParser.DeserializeLLSDXml(request);
            ObjectMediaMessage omm = new ObjectMediaMessage();
            omm.Deserialize(osd);
            
            if (omm.Request is ObjectMediaRequest)
                return HandleObjectMediaRequest(omm.Request as ObjectMediaRequest);
            else if (omm.Request is ObjectMediaUpdate)
                return HandleObjectMediaUpdate(omm.Request as ObjectMediaUpdate);               

            throw new Exception(
                string.Format(
                    "[MOAP]: ObjectMediaMessage has unrecognized ObjectMediaBlock of {0}", 
                    omm.Request.GetType()));
        }
        
        /// <summary>
        /// Handle a request for media textures
        /// </summary>
        /// <param name="omr"></param>
        /// <returns></returns>
        protected string HandleObjectMediaRequest(ObjectMediaRequest omr)       
        {            
            //UUID primId = (UUID)osdParams["object_id"];
            UUID primId = omr.PrimID;
            
            SceneObjectPart part = m_scene.GetSceneObjectPart(primId);
            
            if (null == part)
            {
                m_log.WarnFormat(
                    "[MOAP]: Received a GET ObjectMediaRequest for prim {0} but this doesn't exist in region {1}", 
                    primId, m_scene.RegionInfo.RegionName);
                return string.Empty;
            }
                        
            /*
            int faces = part.GetNumberOfSides();
            m_log.DebugFormat("[MOAP]: Faces [{0}] for [{1}]", faces, primId);
            
            MediaEntry[] media = new MediaEntry[faces];
            for (int i = 0; i < faces; i++)
            {
                MediaEntry me = new MediaEntry();                
                me.HomeURL = "google.com";
                me.CurrentURL = "google.com";
                me.AutoScale = true;
                //me.Height = 300;
                //me.Width = 240;
                media[i] = me;
            }
            */
            
            if (null == part.Shape.Media)
                return string.Empty;
            
            ObjectMediaResponse resp = new ObjectMediaResponse();
            
            resp.PrimID = primId;
            resp.FaceMedia = part.Shape.Media.ToArray();
            resp.Version = part.MediaUrl;
           
            string rawResp = OSDParser.SerializeLLSDXmlString(resp.Serialize());
            
            m_log.DebugFormat("[MOAP]: Got HandleObjectMediaRequestGet raw response is [{0}]", rawResp);
            
            return rawResp;
        }
        
        /// <summary>
        /// Handle an update of media textures.
        /// </summary>
        /// <param name="omu">/param>
        /// <returns></returns>
        protected string HandleObjectMediaUpdate(ObjectMediaUpdate omu)      
        {
            UUID primId = omu.PrimID;
            
            SceneObjectPart part = m_scene.GetSceneObjectPart(primId);
            
            if (null == part)
            {
                m_log.WarnFormat(
                    "[MOAP]: Received an UPDATE ObjectMediaRequest for prim {0} but this doesn't exist in region {1}", 
                    primId, m_scene.RegionInfo.RegionName);
                return string.Empty;
            }            
            
            m_log.DebugFormat("[MOAP]: Received {0} media entries for prim {1}", omu.FaceMedia.Length, primId);
            
            part.Shape.Media = new List<MediaEntry>(omu.FaceMedia);
            
            if (null == part.MediaUrl)
            {
                // TODO: We can't set the last changer until we start tracking which cap we give to which agent id
                part.MediaUrl = "x-mv:0000000000/" + UUID.Zero;
            }
            else
            {
                string rawVersion = part.MediaUrl.Substring(5, 10);
                int version = int.Parse(rawVersion);
                part.MediaUrl = string.Format("x-mv:{0:D10}/{1}", ++version, UUID.Zero);
            }
            
            m_log.DebugFormat("[MOAP]: Storing media url [{0}] in prim {1} {2}", part.MediaUrl, part.Name, part.UUID);
            
            // Arguably we don't need to send a full update to the avatar that just changed the texture.
            part.ScheduleFullUpdate();
            
            return string.Empty;
        }
        
        /// <summary>
        /// Received from the viewer if a user has changed the url of a media texture.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="httpRequest">/param>
        /// <param name="httpResponse">/param>
        /// <returns></returns>
        protected string HandleObjectMediaNavigateMessage(
            string request, string path, string param, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {            
            m_log.DebugFormat("[MOAP]: Got ObjectMediaNavigate request [{0}]", request);
            
            OSDMap osd = (OSDMap)OSDParser.DeserializeLLSDXml(request);
            ObjectMediaNavigateMessage omn = new ObjectMediaNavigateMessage();
            omn.Deserialize(osd);           
            
            UUID primId = omn.PrimID;
            
            SceneObjectPart part = m_scene.GetSceneObjectPart(primId);
            
            if (null == part)
            {
                m_log.WarnFormat(
                    "[MOAP]: Received an ObjectMediaNavigateMessage for prim {0} but this doesn't exist in region {1}", 
                    primId, m_scene.RegionInfo.RegionName);
                return string.Empty;
            }  
            
            m_log.DebugFormat(
                "[MOAP]: Updating media entry for face {0} on prim {1} {2} to {3}", 
                omn.Face, part.Name, part.UUID, omn.URL);
            
            MediaEntry me = part.Shape.Media[omn.Face];
            me.CurrentURL = omn.URL;
            
            string oldMediaUrl = part.MediaUrl;
            
            // TODO: refactor into common method
            string rawVersion = oldMediaUrl.Substring(5, 10);
            int version = int.Parse(rawVersion);
            part.MediaUrl = string.Format("x-mv:{0:D10}/{1}", ++version, UUID.Zero);            
            
            m_log.DebugFormat(
                "[MOAP]: Updating media url in prim {0} {1} from [{2}] to [{3}]", 
                part.Name, part.UUID, oldMediaUrl, part.MediaUrl);
            
            part.ScheduleFullUpdate();
            
            // TODO: Persist in database            
            
            return string.Empty;
        }                   
    }
}