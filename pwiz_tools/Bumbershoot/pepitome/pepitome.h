//
// $Id$
//
// The contents of this file are subject to the Mozilla Public License
// Version 1.1 (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// http://www.mozilla.org/MPL/
//
// Software distributed under the License is distributed on an "AS IS"
// basis, WITHOUT WARRANTY OF ANY KIND, either express or implied. See the
// License for the specific language governing rights and limitations
// under the License.
//
// The Original Code is the MyriMatch search engine.
//
// The Initial Developer of the Original Code is Surendra Dasari.
//
// Copyright 2009 Vanderbilt University
//
// Contributor(s):
//

#ifndef _PEPITOME_H
#define _PEPITOME_H

#include "stdafx.h"
#include "freicore.h"
#include "base64.h"
#include "pepitomeSpectrum.h"
#include "spectraStore.h"
#include <boost/atomic.hpp>
#include <boost/cstdint.hpp>

#define PEPITOME_LICENSE			COMMON_LICENSE

using namespace freicore;

namespace freicore
{

namespace pepitome
{
    struct Version
    {
        static int Major();
        static int Minor();
        static int Revision();
        static std::string str();
        static std::string LastModified();
    };

	typedef multimap< double, pair<Spectrum*, PrecursorMassHypothesis> >   SpectraMassMap;
	typedef vector< SpectraMassMap >									   SpectraMassMapList;

	struct SearchStatistics
	{
		SearchStatistics()
        :   numSpectraSearched(0), numSpectraQueried(0),
			numComparisonsDone(0), numCandidatesSkipped (0)  
        {}

        SearchStatistics(const SearchStatistics& other)
        {
            operator=(other);
        }

        SearchStatistics& operator=(const SearchStatistics& other)
		{
            numSpectraSearched.store(other.numSpectraSearched);
            numSpectraQueried.store(other.numSpectraQueried);
            numComparisonsDone.store(other.numComparisonsDone);
            numCandidatesSkipped.store(other.numCandidatesSkipped);
            return *this;
        }

        boost::atomic_uint32_t numSpectraSearched;
		boost::atomic_uint32_t numSpectraQueried;
		boost::atomic_uint32_t numComparisonsDone;
        boost::atomic_uint32_t numCandidatesSkipped;

		template< class Archive >
		void serialize( Archive& ar, const unsigned int version )
		{
			ar & numSpectraSearched & numSpectraQueried & numComparisonsDone & numCandidatesSkipped;
		}

        SearchStatistics operator+ ( const SearchStatistics& rhs )
		{
			SearchStatistics tmp(*this);
			tmp.numSpectraSearched.fetch_add(rhs.numSpectraSearched);
			tmp.numSpectraQueried.fetch_add(rhs.numSpectraQueried);
			tmp.numComparisonsDone.fetch_add(rhs.numComparisonsDone);
			tmp.numCandidatesSkipped.fetch_add(rhs.numCandidatesSkipped);
			return tmp;
		}

		operator string()
		{
			stringstream s;
			s	<< numSpectraSearched << " spectra; " << numSpectraQueried << " queries; " << numComparisonsDone << " comparisons";
            if(numCandidatesSkipped>0) {
                s << "; " << numCandidatesSkipped << " skipped";
            }
			return s.str();
		}
	};

    typedef map<size_t, vector <SpectraMassMap::iterator> > CandidateQueries;
    typedef pair<size_t, vector<SpectraMassMap::iterator> > Query;
    
	extern proteinStore						proteins;
    extern SpectraStore                     librarySpectra;
	extern SpectraList						spectra;
}
}

#endif
